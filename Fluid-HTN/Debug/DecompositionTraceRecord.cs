using FluidHTN.Compounds;
using FluidHTN.Conditions;

namespace FluidHTN.Debug
{
    // Fork fix 3 (Dauie/numun#283): structured trace records replace the free-text decomposition log.
    // A reader explains a plan from these fields, not from text that upstream can change.

    /// <summary>
    ///     The outcome of one method of a compound task in a decomposition.
    /// </summary>
    public enum TraceOutcome
    {
        /// <summary>The method decomposed, and its tasks are in the plan of the compound task.</summary>
        Succeeded,

        /// <summary>The method decomposed up to a pause of the plan.</summary>
        Partial,

        /// <summary>The method passed its conditions, but its decomposition failed.</summary>
        Failed,

        /// <summary>A condition of the method, or a missing subtask, refused the method.</summary>
        Refused,

        /// <summary>The method cannot beat the method traversal record of the running plan.</summary>
        Rejected,
    }

    /// <summary>
    ///     One record of the decomposition trace: compound task <see cref="Task"/> tried
    ///     <see cref="Method"/> at <see cref="Depth"/>, with <see cref="Outcome"/>.
    ///     For a selector, a method is one subtask. For a sequence, a method is one step of the sequence.
    /// </summary>
    public readonly struct DecompositionTraceRecord
    {
        public DecompositionTraceRecord(int depth, ICompoundTask task, ITask method, TraceOutcome outcome,
            ICondition refusedCondition)
        {
            Depth = depth;
            Task = task;
            Method = method;
            Outcome = outcome;
            RefusedCondition = refusedCondition;
        }

        /// <summary>The decomposition depth of <see cref="Task"/>. The root of the domain is at depth 1.</summary>
        public int Depth { get; }

        /// <summary>The compound task that tried the method. It is null for a task with no parent.</summary>
        public ICompoundTask Task { get; }

        /// <summary>The subtask that the compound task tried.</summary>
        public ITask Method { get; }

        public TraceOutcome Outcome { get; }

        /// <summary>
        ///     The condition that refused the method, or null. A slot with no subtask and a compound
        ///     task with no subtasks are refused with no condition.
        /// </summary>
        public ICondition RefusedCondition { get; }

        public override string ToString()
        {
            return $"{Depth} {Task?.Name} > {Method?.Name}: {Outcome}" +
                   (RefusedCondition != null ? $" ({RefusedCondition.Name})" : "");
        }
    }
}
