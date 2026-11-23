using System;

namespace FluidHTN.Conditions
{
    public class FuncCondition<T> : ICondition where T : IContext
    {
        // ========================================================= FIELDS

        private readonly Func<T, bool> _func;

        // ========================================================= CONSTRUCTION

        public FuncCondition(string name, Func<T, bool> func)
        {
            Name = name;
            _func = func;
        }

        // ========================================================= PROPERTIES

        public string Name { get; }

        // ========================================================= VALIDITY

        public bool IsValid(IContext ctx)
        {
            if (ctx is T c)
            {
                return _func?.Invoke(c) ?? false;
            }

            throw new Exception("Unexpected context type!");
        }
    }
}
