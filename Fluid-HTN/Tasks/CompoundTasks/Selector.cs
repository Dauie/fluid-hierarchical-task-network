using System;
using System.Collections.Generic;
using FluidHTN.Debug;
using FluidHTN.PrimitiveTasks;

namespace FluidHTN.Compounds
{
    public class Selector : CompoundTask
    {
        // ========================================================= FIELDS

        protected readonly Queue<ITask> Plan = new Queue<ITask>();

        // ========================================================= VALIDITY

        public override bool IsValid(IContext ctx)
        {
            // Check that our preconditions are valid first.
            if (base.IsValid(ctx) == false)
            {
                return false;
            }

            // Selector requires there to be at least one sub-task to successfully select from.
            if (Subtasks.Count == 0)
            {
                ctx.AddTrace(Parent, this, TraceOutcome.Refused, null);
                return false;
            }

            return true;
        }

        private bool BeatsLastMTR(IContext ctx, int taskIndex, int currentDecompositionIndex)
        {
            // If the last plan's traversal record for this decomposition layer 
            // has a smaller index than the current task index we're about to
            // decompose, then the new decomposition can't possibly beat the
            // running plan, so we cancel finding a new plan.
            if (ctx.LastMTR[currentDecompositionIndex] < taskIndex)
            {
                // But, if any of the earlier records beat the record in LastMTR, we're still good, as we're on a higher priority branch.
                // This ensures that [0,0,1] can beat [0,1,0]
                for (var i = 0; i < ctx.MethodTraversalRecord.Count; i++)
                {
                    var diff = ctx.MethodTraversalRecord[i] - ctx.LastMTR[i];

                    if (diff < 0)
                    {
                        return true;
                    }

                    if (diff > 0)
                    {
                        // We should never really be able to get here, but just in case.
                        return false;
                    }
                }

                return false;
            }

            return true;
        }

        // ========================================================= DECOMPOSITION

        /// <summary>
        ///     In a Selector decomposition, just a single sub-task must be valid and successfully decompose for the Selector to be
        ///     successfully decomposed.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        protected override DecompositionStatus OnDecompose(IContext ctx, int startIndex, out Queue<ITask> result)
        {
            Plan.Clear();

            for (var taskIndex = startIndex; taskIndex < Subtasks.Count; taskIndex++)
            {
                // If the last plan is still running, we need to check whether the
                // new decomposition can possibly beat it.
                if (ctx.LastMTR != null && ctx.LastMTR.Count > 0)
                {
                    if (ctx.MethodTraversalRecord.Count < ctx.LastMTR.Count)
                    {
                        var currentDecompositionIndex = ctx.MethodTraversalRecord.Count;
                        if (BeatsLastMTR(ctx, taskIndex, currentDecompositionIndex) == false)
                        {
                            ctx.MethodTraversalRecord.Add(-1);
                            if (ctx.DebugMTR)
                            {
                                ctx.MTRDebug.Add($"REPLAN FAIL {Subtasks[taskIndex].Name}");
                            }

                            ctx.AddTrace(this, Subtasks[taskIndex], TraceOutcome.Rejected, null);
                            result = null;
                            return DecompositionStatus.Rejected;
                        }
                    }
                }

                var task = Subtasks[taskIndex];

                // Fork fix 2 (Dauie/numun#283): a condition can select a value and then a later
                // condition can fail. No world state changed, but the value must go. One int per
                // attempt records the binding depth, so the selector allocates nothing more.
                var oldBindingDepth = ctx.BindingDepth;

                // Selector passes null for oldStackDepth: each subtask alternative is independent,
                // so there are no accumulated world state effects to roll back between attempts.
                var status = OnDecomposeTask(ctx, task, taskIndex, null, out result);
                switch (status)
                {
                    case DecompositionStatus.Succeeded:
                    case DecompositionStatus.Partial:
                        return status;
                    case DecompositionStatus.Rejected:
                        ctx.TrimBindings(oldBindingDepth);
                        return status;
                    case DecompositionStatus.Failed:
                    default:
                        ctx.TrimBindings(oldBindingDepth);
                        continue;
                }
            }

            result = Plan;
            return result.Count == 0 ? DecompositionStatus.Failed : DecompositionStatus.Succeeded;
        }

        protected override DecompositionStatus OnDecomposeTask(IContext ctx, ITask task, int taskIndex,
            int[] oldStackDepth, out Queue<ITask> result)
        {
            if (task.IsValid(ctx) == false)
            {
                result = Plan;
                return task.OnIsValidFailed(ctx);
            }

            // A refused task added its own trace record in IsValid. Each other path adds one here.
            if (task is ICompoundTask compoundTask)
            {
                return Trace(ctx, task, OnDecomposeCompoundTask(ctx, compoundTask, taskIndex, null, out result));
            }

            if (task is IPrimitiveTask primitiveTask)
            {
                OnDecomposePrimitiveTask(ctx, primitiveTask, taskIndex, null, out result);
            }

            if (task is Slot slot)
            {
                return Trace(ctx, task, OnDecomposeSlot(ctx, slot, taskIndex, null, out result));
            }

            result = Plan;
            var status = result.Count == 0 ? DecompositionStatus.Failed : DecompositionStatus.Succeeded;
            return Trace(ctx, task, status);
        }

        protected override void OnDecomposePrimitiveTask(IContext ctx, IPrimitiveTask task, int taskIndex,
            int[] oldStackDepth, out Queue<ITask> result)
        {
            // We need to record the task index before we decompose the task,
            // so that the traversal record is set up in the right order.
            ctx.MethodTraversalRecord.Add(taskIndex);
            if (ctx.DebugMTR)
            {
                ctx.MTRDebug.Add(task.Name);
            }

            task.ApplyEffects(ctx);
            Plan.Enqueue(task);
            result = Plan;
        }

        protected override DecompositionStatus OnDecomposeCompoundTask(IContext ctx, ICompoundTask task, int taskIndex,
            int[] oldStackDepth, out Queue<ITask> result)
        {
            // We need to record the task index before we decompose the task,
            // so that the traversal record is set up in the right order.
            ctx.MethodTraversalRecord.Add(taskIndex);

            if (ctx.DebugMTR)
            {
                ctx.MTRDebug.Add(task.Name);
            }

            var status = task.Decompose(ctx, 0, out var subPlan);

            // If status is rejected, that means the entire planning procedure should cancel.
            if (status == DecompositionStatus.Rejected)
            {
                result = null;
                return DecompositionStatus.Rejected;
            }

            // If the decomposition failed
            if (status == DecompositionStatus.Failed)
            {
                // Remove the taskIndex if it failed to decompose.
                ctx.MethodTraversalRecord.RemoveAt(ctx.MethodTraversalRecord.Count - 1);
                if (ctx.DebugMTR)
                {
                    ctx.MTRDebug.RemoveAt(ctx.MTRDebug.Count - 1);
                }

                result = Plan;
                return DecompositionStatus.Failed;
            }

            while (subPlan.Count > 0)
            {
                var p = subPlan.Dequeue();

                Plan.Enqueue(p);
            }

            if (ctx.HasPausedPartialPlan)
            {
                result = Plan;
                return DecompositionStatus.Partial;
            }

            result = Plan;
            var s = result.Count == 0 ? DecompositionStatus.Failed : DecompositionStatus.Succeeded;

            return s;
        }

        protected override DecompositionStatus OnDecomposeSlot(IContext ctx, Slot task, int taskIndex, int[] oldStackDepth, out Queue<ITask> result)
        {
            // We need to record the task index before we decompose the task,
            // so that the traversal record is set up in the right order.
            ctx.MethodTraversalRecord.Add(taskIndex);

            if (ctx.DebugMTR)
            {
                ctx.MTRDebug.Add(task.Name);
            }

            var status = task.Decompose(ctx, 0, out var subPlan);

            // If status is rejected, that means the entire planning procedure should cancel.
            if (status == DecompositionStatus.Rejected)
            {
                result = null;
                return DecompositionStatus.Rejected;
            }

            // If the decomposition failed
            if (status == DecompositionStatus.Failed)
            {
                // Remove the taskIndex if it failed to decompose.
                ctx.MethodTraversalRecord.RemoveAt(ctx.MethodTraversalRecord.Count - 1);
                if (ctx.DebugMTR)
                {
                    ctx.MTRDebug.RemoveAt(ctx.MTRDebug.Count - 1);
                }

                result = Plan;
                return DecompositionStatus.Failed;
            }

            while (subPlan.Count > 0)
            {
                var p = subPlan.Dequeue();

                Plan.Enqueue(p);
            }

            if (ctx.HasPausedPartialPlan)
            {
                result = Plan;
                return DecompositionStatus.Partial;
            }

            result = Plan;
            var s = result.Count == 0 ? DecompositionStatus.Failed : DecompositionStatus.Succeeded;

            return s;
        }
    }
}
