using FluidHTN;
using FluidHTN.Compounds;
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
    }
}
