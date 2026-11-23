using System;
using System.Collections.Generic;
using FluidHTN.Conditions;
using FluidHTN.Debug;
using FluidHTN.Factory;
using FluidHTN.Compounds;

namespace FluidHTN.Contexts
{
    public abstract class BaseContext : IContext
    {
        // ========================================================= PROPERTIES

        public bool IsInitialized { get; protected set; } = false;
        public bool IsDirty { get; set; }
        public ContextState ContextState { get; set; } = ContextState.Executing;
        public int CurrentDecompositionDepth { get; set; } = 0;
        public abstract IFactory Factory { get; protected set; }
        public abstract IPlannerState PlannerState { get; protected set; }
        public List<int> MethodTraversalRecord { get; set; } = new List<int>();
        public List<int> LastMTR { get; } = new List<int>();
        public abstract List<string> MTRDebug { get; set; }
        public abstract List<string> LastMTRDebug { get; set; }
        public abstract bool DebugMTR { get; }
        public List<DecompositionTraceRecord> DecompositionTrace { get; set; }
        public abstract bool LogDecomposition { get; }
        public Queue<PartialPlanEntry> PartialPlanQueue { get; set; } = new Queue<PartialPlanEntry>();
        public bool HasPausedPartialPlan { get; set; } = false;

        public abstract byte[] WorldState { get; }

        public Stack<KeyValuePair<EffectType, byte>>[] WorldStateChangeStack { get; protected set; }

        // The binding stack (fork fix 2, Dauie/numun#283). A list, so that a read can go from the
        // newest value to the oldest by index.
        private List<KeyValuePair<int, long>> _bindings;

        // ========================================================= INITIALIZATION

        public virtual void Init()
        {
            if (WorldStateChangeStack == null)
            {
                WorldStateChangeStack = new Stack<KeyValuePair<EffectType, byte>>[WorldState.Length];
                for (var i = 0; i < WorldState.Length; i++)
                {
                    WorldStateChangeStack[i] = new Stack<KeyValuePair<EffectType, byte>>();
                }
            }

            if (_bindings == null)
            {
                _bindings = new List<KeyValuePair<int, long>>();
            }

            if (DebugMTR)
            {
                if (MTRDebug == null)
                {
                    MTRDebug = new List<string>();
                }

                if (LastMTRDebug == null)
                {
                    LastMTRDebug = new List<string>();
                }
            }

            if (LogDecomposition)
            {
                if (DecompositionTrace == null)
                {
                    DecompositionTrace = new List<DecompositionTraceRecord>();
                }
            }

            IsInitialized = true;
        }

        // ========================================================= STATE HANDLING

        public bool HasState(int state, byte value)
        {
            return GetState(state) == value;
        }

        public byte GetState(int state)
        {
            if (ContextState == ContextState.Executing)
            {
                return WorldState[state];
            }

            if (WorldStateChangeStack[state].Count == 0)
            {
                return WorldState[state];
            }

            return WorldStateChangeStack[state].Peek().Value;
        }

        public virtual void SetState(int state, byte value, bool setAsDirty = true, EffectType e = EffectType.Permanent)
        {
            if (ContextState == ContextState.Executing)
            {
                // Prevent setting the world state dirty if we're not changing anything.
                if (WorldState[state] == value)
                {
                    return;
                }

                WorldState[state] = value;
                if (setAsDirty)
                {
                    IsDirty = true; // When a state change during execution, we need to mark the context dirty for replanning!
                }
            }
            else
            {
                WorldStateChangeStack[state].Push(new KeyValuePair<EffectType, byte>(e, value));
            }
        }

        // ========================================================= STATE STACK HANDLING

        public int[] GetWorldStateChangeDepth(IFactory factory)
        {
            var stackDepth = factory.CreateArray<int>(WorldStateChangeStack.Length);

            for (var i = 0; i < WorldStateChangeStack.Length; i++)
            {
                stackDepth[i] = WorldStateChangeStack[i]?.Count ?? 0;
            }

            return stackDepth;
        }

        public void TrimForExecution()
        {
            if (ContextState == ContextState.Executing)
            {
                throw new Exception("Can not trim a context when in execution mode");
            }

            foreach (var stack in WorldStateChangeStack)
            {
                while (stack.Count != 0 && stack.Peek().Key != EffectType.Permanent)
                {
                    stack.Pop();
                }
            }
        }

        public void TrimToStackDepth(int[] stackDepth)
        {
            if (ContextState == ContextState.Executing)
            {
                throw new Exception("Can not trim a context when in execution mode");
            }

            for (var i = 0; i < stackDepth.Length; i++)
            {
                var stack = WorldStateChangeStack[i];

                while (stack.Count > stackDepth[i])
                {
                    stack.Pop();
                }
            }
        }

        // ========================================================= BINDING STACK HANDLING

        public int BindingDepth => _bindings?.Count ?? 0;

        public void SetBinding(int key, long value)
        {
            // The executor checks the conditions of a task again during execution. A binding is a
            // planning value, so a condition that selects a value then keeps the values of the plan.
            if (ContextState == ContextState.Executing)
            {
                return;
            }

            _bindings.Add(new KeyValuePair<int, long>(key, value));
        }

        public bool TryGetBinding(int key, out long value)
        {
            if (_bindings != null)
            {
                for (var i = _bindings.Count - 1; i >= 0; i--)
                {
                    if (_bindings[i].Key == key)
                    {
                        value = _bindings[i].Value;
                        return true;
                    }
                }
            }

            value = 0;
            return false;
        }

        public void TrimBindings(int depth)
        {
            // No check of the context state or of Init: a compound task can decompose in the
            // executing state and before Init (the upstream tests do). A trim then has no values to remove.
            if (_bindings != null && _bindings.Count > depth)
            {
                _bindings.RemoveRange(depth, _bindings.Count - depth);
            }
        }

        // ========================================================= STATE RESET

        public virtual void Reset()
        {
            MethodTraversalRecord?.Clear();
            LastMTR?.Clear();

            if (DebugMTR)
            {
                MTRDebug?.Clear();
                LastMTRDebug?.Clear();
            }

            IsInitialized = false;
        }

        // ========================================================= DECOMPOSITION TRACE

        public void AddTrace(ICompoundTask task, ITask method, TraceOutcome outcome, ICondition refusedCondition)
        {
            if (LogDecomposition == false)
            {
                return;
            }

            DecompositionTrace.Add(new DecompositionTraceRecord(CurrentDecompositionDepth, task, method, outcome,
                refusedCondition));
        }
    }
}
