using System;
using System.Collections.Generic;
using FluidHTN.Conditions;
using FluidHTN.PrimitiveTasks;
using FluidHTN.Debug;

namespace FluidHTN.Compounds
{
    public abstract class CompoundTask : ICompoundTask
    {
        // ========================================================= PROPERTIES

        public string Name { get; set; }
        public ICompoundTask Parent { get; set; }
        public List<ICondition> Conditions { get; } = new List<ICondition>();
        public List<ITask> Subtasks { get; } = new List<ITask>();

        // ========================================================= VALIDITY

        public virtual DecompositionStatus OnIsValidFailed(IContext ctx)
        {
            return DecompositionStatus.Failed;
        }

        // ========================================================= ADDERS

        public ITask AddCondition(ICondition condition)
        {
            Conditions.Add(condition);
            return this;
        }

        public ICompoundTask AddSubtask(ITask subtask)
        {
            Subtasks.Add(subtask);
            return this;
        }

        // ========================================================= DECOMPOSITION

        public DecompositionStatus Decompose(IContext ctx, int startIndex, out Queue<ITask> result)
        {
            if (ctx.LogDecomposition)
            {
                ctx.CurrentDecompositionDepth++;
            }

            var status = OnDecompose(ctx, startIndex, out result);
            
            if (ctx.LogDecomposition)
            {
                ctx.CurrentDecompositionDepth--;
            }

            return status;
        }

        protected abstract DecompositionStatus OnDecompose(IContext ctx, int startIndex, out Queue<ITask> result);

        // oldStackDepth is a snapshot of world state change stack depths taken before the
        // decomposition loop began (see Sequence.OnDecompose). It is threaded through every
        // helper so that a Sequence can roll back all accumulated world state changes on failure.
        // Selector always passes null here because it never accumulates effects across alternatives.
        protected abstract DecompositionStatus OnDecomposeTask(IContext ctx, ITask task, int taskIndex, int[] oldStackDepth, out Queue<ITask> result);

        protected abstract void OnDecomposePrimitiveTask(IContext ctx, IPrimitiveTask task, int taskIndex, int[] oldStackDepth, out Queue<ITask> result);

        protected abstract DecompositionStatus OnDecomposeCompoundTask(IContext ctx, ICompoundTask task, int taskIndex, int[] oldStackDepth, out Queue<ITask> result);

        protected abstract DecompositionStatus OnDecomposeSlot(IContext ctx, Slot task, int taskIndex, int[] oldStackDepth, out Queue<ITask> result);

        // ========================================================= VALIDITY

        public virtual bool IsValid(IContext ctx)
        {
            foreach (var condition in Conditions)
            {
                if (condition.IsValid(ctx) == false)
                {
                    ctx.AddTrace(Parent, this, TraceOutcome.Refused, condition);
                    return false;
                }
            }

            return true;
        }

        // ========================================================= TRACE

        /// <summary>
        ///     Add a trace record for a method of this task that passed its conditions, and return the status.
        ///     A method that its conditions refuse adds its own record in IsValid, with the condition.
        /// </summary>
        protected DecompositionStatus Trace(IContext ctx, ITask method, DecompositionStatus status)
        {
            ctx.AddTrace(this, method, ToTraceOutcome(status), null);
            return status;
        }

        private static TraceOutcome ToTraceOutcome(DecompositionStatus status)
        {
            switch (status)
            {
                case DecompositionStatus.Succeeded:
                    return TraceOutcome.Succeeded;
                case DecompositionStatus.Partial:
                    return TraceOutcome.Partial;
                case DecompositionStatus.Rejected:
                    return TraceOutcome.Rejected;
                default:
                    return TraceOutcome.Failed;
            }
        }
    }
}
