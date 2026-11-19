using System;
using System.Collections.Generic;
using FluidHTN.Debug;
using FluidHTN.PrimitiveTasks;

namespace FluidHTN.Compounds
{
    public class Sequence : CompoundTask, IDecomposeAll
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

            // Selector requires there to be subtasks to successfully select from.
            if (Subtasks.Count == 0)
            {
                ctx.AddTrace(Parent, this, TraceOutcome.Refused, null);
                return false;
            }

            return true;
        }

        // ========================================================= DECOMPOSITION

        /// <summary>
        ///     In a Sequence decomposition, all sub-tasks must be valid and successfully decomposed in order for the Sequence to
        ///     be successfully decomposed.
        /// </summary>
        /// <param name="ctx"></param>
        /// <returns></returns>
        protected override DecompositionStatus OnDecompose(IContext ctx, int startIndex, out Queue<ITask> result)
        {
            Plan.Clear();

            // Snapshot the current world state change stack depths before we begin decomposing
            // subtasks. Because a Sequence must succeed as a whole, any world state effects applied
            // during a failed subtask decomposition must be rolled back. We capture this snapshot
            // here — outside the subtask loop — so that TrimToStackDepth can undo all accumulated
            // changes across every subtask attempted so far.
            var oldStackDepth = ctx.GetWorldStateChangeDepth(ctx.Factory);

            // Fork fix 2 (Dauie/numun#283): the binding stack rolls back with the world state.
            // A failed or rejected sequence removes the values that its subtasks selected.
            var oldBindingDepth = ctx.BindingDepth;

            for (var taskIndex = startIndex; taskIndex < Subtasks.Count; taskIndex++)
            {
                var task = Subtasks[taskIndex];

                var status = OnDecomposeTask(ctx, task, taskIndex, oldStackDepth, out result);
                switch (status)
                {
                    case DecompositionStatus.Rejected:
                    case DecompositionStatus.Failed:
                    {
                        ctx.TrimBindings(oldBindingDepth);
                        ctx.Factory.FreeArray(ref oldStackDepth);
                        return status;
                    }
                    case DecompositionStatus.Partial:
                    {
                        ctx.Factory.FreeArray(ref oldStackDepth);
                        return status;
                    }
                }
            }

            ctx.Factory.FreeArray(ref oldStackDepth);

            result = Plan;
            if (result.Count == 0)
            {
                ctx.TrimBindings(oldBindingDepth);
                return DecompositionStatus.Failed;
            }

            return DecompositionStatus.Succeeded;
        }

        protected override DecompositionStatus OnDecomposeTask(IContext ctx, ITask task, int taskIndex,
            int[] oldStackDepth, out Queue<ITask> result)
        {
            if (task.IsValid(ctx) == false)
            {
                Plan.Clear();
                ctx.TrimToStackDepth(oldStackDepth);
                result = Plan;
                return task.OnIsValidFailed(ctx);
            }

            // A refused task added its own trace record in IsValid. Each other path adds one here.
            if (task is ICompoundTask compoundTask)
            {
                return Trace(ctx, task, OnDecomposeCompoundTask(ctx, compoundTask, taskIndex, oldStackDepth, out result));
            }
            else if (task is IPrimitiveTask primitiveTask)
            {
                OnDecomposePrimitiveTask(ctx, primitiveTask, taskIndex, oldStackDepth, out result);
            }
            else if (task is PausePlanTask)
            {
                ctx.HasPausedPartialPlan = true;
                ctx.PartialPlanQueue.Enqueue(new PartialPlanEntry()
                {
                    Task = this,
                    TaskIndex = taskIndex + 1,
                });

                result = Plan;
                return Trace(ctx, task, DecompositionStatus.Partial);
            }
            else if (task is Slot slot)
            {
                return Trace(ctx, task, OnDecomposeSlot(ctx, slot, taskIndex, oldStackDepth, out result));
            }

            result = Plan;
            var s = result.Count == 0 ? DecompositionStatus.Failed : DecompositionStatus.Succeeded;
            return Trace(ctx, task, s);
        }

        protected override void OnDecomposePrimitiveTask(IContext ctx, IPrimitiveTask task, int taskIndex,
            int[] oldStackDepth, out Queue<ITask> result)
        {
            // oldStackDepth is not used here; rollback of primitive task effects is handled
            // at the Sequence level (in OnDecomposeTask/OnDecomposeCompoundTask) via TrimToStackDepth.

            // We don't add MTR tracking on sequences for primary sub-tasks, since they will always be included, so they're irrelevant to MTR tracking.

            task.ApplyEffects(ctx);
            Plan.Enqueue(task);
            result = Plan;
        }

        protected override DecompositionStatus OnDecomposeCompoundTask(IContext ctx, ICompoundTask task,
            int taskIndex, int[] oldStackDepth, out Queue<ITask> result)
        {
            var status = task.Decompose(ctx, 0, out var subPlan);

            // If result is null, that means the entire planning procedure should cancel.
            if (status == DecompositionStatus.Rejected)
            {
                Plan.Clear();
                ctx.TrimToStackDepth(oldStackDepth);

                result = null;
                return DecompositionStatus.Rejected;
            }

            // If the decomposition failed
            if (status == DecompositionStatus.Failed)
            {
                Plan.Clear();
                ctx.TrimToStackDepth(oldStackDepth);
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
                if (taskIndex < Subtasks.Count - 1)
                {
                    ctx.PartialPlanQueue.Enqueue(new PartialPlanEntry()
                    {
                        Task = this,
                        TaskIndex = taskIndex + 1,
                    });
                }

                result = Plan;
                return DecompositionStatus.Partial;
            }

            result = Plan;

            return DecompositionStatus.Succeeded;
        }

        protected override DecompositionStatus OnDecomposeSlot(IContext ctx, Slot task,
            int taskIndex, int[] oldStackDepth, out Queue<ITask> result)
        {
            var status = task.Decompose(ctx, 0, out var subPlan);

            // If result is null, that means the entire planning procedure should cancel.
            if (status == DecompositionStatus.Rejected)
            {
                Plan.Clear();
                ctx.TrimToStackDepth(oldStackDepth);

                result = null;
                return DecompositionStatus.Rejected;
            }

            // If the decomposition failed
            if (status == DecompositionStatus.Failed)
            {
                Plan.Clear();
                ctx.TrimToStackDepth(oldStackDepth);
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
                if (taskIndex < Subtasks.Count - 1)
                {
                    ctx.PartialPlanQueue.Enqueue(new PartialPlanEntry()
                    {
                        Task = this,
                        TaskIndex = taskIndex + 1,
                    });
                }

                result = Plan;
                return DecompositionStatus.Partial;
            }

            result = Plan;

            return DecompositionStatus.Succeeded;
        }
    }
}
