using System.Collections.Generic;
using FluidHTN;
using FluidHTN.Compounds;
using FluidHTN.Conditions;
using FluidHTN.PrimitiveTasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fluid_HTN.UnitTests
{
    /// <summary>
    /// Fork fix 2 (Dauie/numun#283): a binding stack holds the values that a branch selects,
    /// and a failed branch trims it with the world-state stacks.
    /// Upstream had no such stack. A value in a context field stayed after its branch failed.
    /// </summary>
    [TestClass]
    public class BindingTests
    {
        private const int Partner = 0;
        private const int Goods = 1;

        private static MyContext NewContext()
        {
            var ctx = new MyContext();
            ctx.Init();
            return ctx;
        }

        /// <summary>
        /// The spike case: a sequence selects a partner, then a later subtask fails.
        /// The selector takes the next method, and the partner of the failed sequence goes.
        /// </summary>
        [TestMethod]
        public void FailedSequenceLeavesNoSelectedValue()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Sequence("Trade")
                        .Action("Pick partner")
                            .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                        .End()
                        .Action("Barter")
                            .Condition("Partner has goods", ctx => false)
                        .End()
                    .End()
                    .Action("Wait")
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            var status = domain.FindPlan(ctx, out var plan);

            Assert.AreEqual(DecompositionStatus.Succeeded, status);
            Assert.AreEqual("Wait", plan.Dequeue().Name);
            Assert.IsFalse(ctx.TryGetBinding(Partner, out _));
            Assert.AreEqual(0, ctx.BindingDepth);
        }

        /// <summary>
        /// A primitive task directly under a selector selects a value in its first condition,
        /// and its second condition fails. No sequence is between them, so the selector trims.
        /// </summary>
        [TestMethod]
        public void FailedPrimitiveUnderSelectorLeavesNoSelectedValue()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Action("Barter")
                        .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                        .Condition("Partner has goods", ctx => false)
                    .End()
                    .Action("Wait")
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            var status = domain.FindPlan(ctx, out var plan);

            Assert.AreEqual(DecompositionStatus.Succeeded, status);
            Assert.AreEqual("Wait", plan.Dequeue().Name);
            Assert.IsFalse(ctx.TryGetBinding(Partner, out _));
        }

        /// <summary>
        /// A failed inner method trims only its own values. The value that the outer sequence
        /// selected before it stays, and the plan uses the next inner method.
        /// </summary>
        [TestMethod]
        public void FailedInnerMethodKeepsOuterValue()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Sequence("Trade")
                    .Action("Pick partner")
                        .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                    .End()
                    .Select("Offer")
                        .Action("Offer goods")
                            .Condition("Select goods 3", ctx => { ctx.SetBinding(Goods, 3); return true; })
                            .Condition("Goods are wanted", ctx => false)
                        .End()
                        .Action("Ask for help")
                        .End()
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            var status = domain.FindPlan(ctx, out var plan);

            Assert.AreEqual(DecompositionStatus.Succeeded, status);
            Assert.AreEqual(2, plan.Count);
            Assert.IsTrue(ctx.TryGetBinding(Partner, out var partner));
            Assert.AreEqual(7L, partner);
            Assert.IsFalse(ctx.TryGetBinding(Goods, out _));
        }

        /// <summary>
        /// The values of a successful plan stay for the caller to read. The top value of a key wins.
        /// </summary>
        [TestMethod]
        public void SuccessfulPlanKeepsSelectedValues()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Sequence("Trade")
                    .Action("Pick partner")
                        .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                    .End()
                    .Action("Pick other partner")
                        .Condition("Select partner 8", ctx => { ctx.SetBinding(Partner, 8); return true; })
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            var status = domain.FindPlan(ctx, out _);

            Assert.AreEqual(DecompositionStatus.Succeeded, status);
            Assert.AreEqual(2, ctx.BindingDepth);
            Assert.IsTrue(ctx.TryGetBinding(Partner, out var partner));
            Assert.AreEqual(8L, partner);
        }

        /// <summary>
        /// A failed plan leaves no value, and the next plan call starts with no value.
        /// </summary>
        [TestMethod]
        public void FailedPlanAndNextPlanStartWithNoValue()
        {
            var fail = true;
            var domain = new DomainBuilder<MyContext>("Test")
                .Sequence("Trade")
                    .Action("Pick partner")
                        .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                    .End()
                    .Action("Barter")
                        .Condition("Partner has goods", ctx => fail == false)
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            // Upstream gives Rejected here, not Failed: the empty traversal record equals the empty last record.
            Assert.AreNotEqual(DecompositionStatus.Succeeded, domain.FindPlan(ctx, out _));
            Assert.AreEqual(0, ctx.BindingDepth);

            fail = false;
            Assert.AreEqual(DecompositionStatus.Succeeded, domain.FindPlan(ctx, out _));
            Assert.AreEqual(1, ctx.BindingDepth);
            Assert.AreEqual(DecompositionStatus.Succeeded, domain.FindPlan(ctx, out _));
            Assert.AreEqual(1, ctx.BindingDepth);
        }

        /// <summary>
        /// A binding is a planning value. In the executing state, SetBinding does nothing, so the
        /// executor's second check of a condition keeps the values of the plan.
        /// </summary>
        [TestMethod]
        public void SetBindingInExecutingStateKeepsPlanValues()
        {
            var ctx = NewContext();
            ctx.ContextState = ContextState.Planning;
            ctx.SetBinding(Partner, 7);
            ctx.ContextState = ContextState.Executing;

            ctx.SetBinding(Partner, 8);

            Assert.AreEqual(1, ctx.BindingDepth);
            Assert.IsTrue(ctx.TryGetBinding(Partner, out var partner));
            Assert.AreEqual(7L, partner);
        }

        /// <summary>
        /// A sequence with no selector above it: the sequence trim alone must remove the value.
        /// This is the case when FindPlan continues a paused sequence. The test decomposes the sequence directly.
        /// </summary>
        [TestMethod]
        public void FailedSequenceTrimsItsOwnValue()
        {
            var ctx = NewContext();
            ctx.ContextState = ContextState.Planning;
            var trade = new Sequence() { Name = "Trade" };
            trade.AddSubtask(new PrimitiveTask() { Name = "Pick partner" }
                .AddCondition(new FuncCondition<MyContext>("Select partner 7", c => { c.SetBinding(Partner, 7); return true; })));
            trade.AddSubtask(new PrimitiveTask() { Name = "Barter" }
                .AddCondition(new FuncCondition<MyContext>("Partner has goods", c => false)));

            var status = trade.Decompose(ctx, 0, out _);

            Assert.AreEqual(DecompositionStatus.Failed, status);
            Assert.AreEqual(0, ctx.BindingDepth);
        }

        /// <summary>
        /// A sequence step succeeds with an empty plan after it selects a value. The sequence then
        /// fails, so it must remove the value. No built-in task does this, so the test uses a custom task.
        /// </summary>
        [TestMethod]
        public void SequenceWithEmptyPlanTrimsItsValue()
        {
            var ctx = NewContext();
            ctx.ContextState = ContextState.Planning;
            var trade = new Sequence() { Name = "Trade" };
            trade.AddSubtask(new EmptyPlanTask(Partner, 7));

            var status = trade.Decompose(ctx, 0, out _);

            Assert.AreEqual(DecompositionStatus.Failed, status);
            Assert.AreEqual(0, ctx.BindingDepth);
        }

        /// <summary>
        /// The condition of an inner selector selects a value. The inner selector then rejects,
        /// because it cannot beat the running plan. The outer selector trim alone must remove the value.
        /// </summary>
        [TestMethod]
        public void RejectedMethodUnderSelectorLeavesNoSelectedValue()
        {
            var ctx = NewContext();
            ctx.ContextState = ContextState.Planning;
            var choose = new Selector() { Name = "Choose" };
            var inner = new Selector() { Name = "Inner" };
            inner.AddCondition(new FuncCondition<MyContext>("Select partner 7", c => { c.SetBinding(Partner, 7); return true; }));
            inner.AddSubtask(new PrimitiveTask() { Name = "Barter" }
                .AddCondition(new FuncCondition<MyContext>("Partner has goods", c => false)));
            inner.AddSubtask(new PrimitiveTask() { Name = "Wait" });
            choose.AddSubtask(inner);
            ctx.LastMTR.Add(0);
            ctx.LastMTR.Add(0);

            var status = choose.Decompose(ctx, 0, out _);

            Assert.AreEqual(DecompositionStatus.Rejected, status);
            Assert.AreEqual(0, ctx.BindingDepth);
        }

        /// <summary>
        /// The decomposition succeeds, but it finds the plan that runs now, so FindPlan rejects it.
        /// No compound task trims in this case. The FindPlan trim after a failed plan must remove the value.
        /// </summary>
        [TestMethod]
        public void SamePlanRejectionLeavesNoSelectedValue()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Action("Barter")
                        .Condition("Select partner 7", ctx => { ctx.SetBinding(Partner, 7); return true; })
                    .End()
                .End()
                .Build();
            var ctx = NewContext();
            ctx.LastMTR.Add(0);
            ctx.LastMTR.Add(0);

            var status = domain.FindPlan(ctx, out _);

            Assert.AreEqual(DecompositionStatus.Rejected, status);
            Assert.AreEqual(0, ctx.BindingDepth);
        }

        /// <summary>
        /// A custom compound task that selects a value and succeeds with an empty plan.
        /// </summary>
        private class EmptyPlanTask : Selector
        {
            private readonly int _key;
            private readonly long _value;

            public EmptyPlanTask(int key, long value)
            {
                _key = key;
                _value = value;
                Name = "Empty plan";

                // A selector with no subtasks is not valid, so the task gets one that it does not use.
                AddSubtask(new PrimitiveTask() { Name = "Unused" });
            }

            protected override DecompositionStatus OnDecompose(IContext ctx, int startIndex, out Queue<ITask> result)
            {
                ctx.SetBinding(_key, _value);
                result = new Queue<ITask>();
                return DecompositionStatus.Succeeded;
            }
        }
    }
}
