using System.Collections.Generic;
using FluidHTN;
using FluidHTN.Compounds;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fluid_HTN.UnitTests
{
    /// <summary>
    /// Fork fix 1 (Dauie/numun#283): FindPlan returns a new plan queue that the caller owns.
    /// Upstream returned the queue of a domain node, and the next plan call cleared it.
    /// </summary>
    [TestClass]
    public class PlanOwnerTests
    {
        private static Domain<MyContext> BuildDomain()
        {
            return new DomainBuilder<MyContext>("Test")
                .Sequence("Trade")
                    .Action("Go to partner")
                    .End()
                    .Action("Barter")
                    .End()
                .End()
                .Build();
        }

        private static Queue<ITask> Plan(Domain<MyContext> domain)
        {
            var ctx = new MyContext();
            ctx.Init();
            var status = domain.FindPlan(ctx, out var plan);
            Assert.AreEqual(DecompositionStatus.Succeeded, status);
            return plan;
        }

        /// <summary>
        /// Two plan calls on one domain return two queues. A change of one queue changes no other queue.
        /// </summary>
        [TestMethod]
        public void TwoPlanCallsReturnTwoQueues()
        {
            var domain = BuildDomain();

            var first = Plan(domain);
            var second = Plan(domain);

            Assert.AreNotSame(first, second);
            first.Clear();
            Assert.AreEqual(2, second.Count);
            Assert.AreEqual("Go to partner", second.Dequeue().Name);
            Assert.AreEqual("Barter", second.Dequeue().Name);
        }

        /// <summary>
        /// A change of a returned queue changes no state of the domain: the next plan call gives the same plan.
        /// </summary>
        [TestMethod]
        public void ChangeOfReturnedQueueChangesNoDomainState()
        {
            var domain = BuildDomain();

            var first = Plan(domain);
            first.Dequeue();
            first.Enqueue(new FluidHTN.PrimitiveTasks.PrimitiveTask() { Name = "Foreign" });

            var second = Plan(domain);
            Assert.AreEqual(2, second.Count);
            Assert.AreEqual("Go to partner", second.Dequeue().Name);
            Assert.AreEqual("Barter", second.Dequeue().Name);
        }
    }
}
