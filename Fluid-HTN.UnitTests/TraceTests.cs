using System.Collections.Generic;
using FluidHTN;
using FluidHTN.Compounds;
using FluidHTN.Conditions;
using FluidHTN.Debug;
using FluidHTN.PrimitiveTasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Fluid_HTN.UnitTests
{
    /// <summary>
    /// Fork fix 3 (Dauie/numun#283): the decomposition writes structured trace records
    /// (Depth, Task, Method, Outcome, RefusedCondition). Upstream wrote free text with a console colour.
    /// </summary>
    [TestClass]
    public class TraceTests
    {
        private static MyDebugContext NewContext()
        {
            var ctx = new MyDebugContext();
            ctx.Init();
            return ctx;
        }

        private static void AssertRecord(DecompositionTraceRecord record, int depth, string task, string method,
            TraceOutcome outcome, string refusedCondition)
        {
            Assert.AreEqual(depth, record.Depth, record.ToString());
            Assert.AreEqual(task, record.Task?.Name, record.ToString());
            Assert.AreEqual(method, record.Method.Name, record.ToString());
            Assert.AreEqual(outcome, record.Outcome, record.ToString());
            Assert.AreEqual(refusedCondition, record.RefusedCondition?.Name, record.ToString());
        }

        /// <summary>
        /// A condition refuses the first method. The record names the condition object,
        /// so a reader can explain the plan without the text of a log.
        /// </summary>
        [TestMethod]
        public void RefusedMethodRecordsItsCondition()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Action("Barter")
                        .Condition("Partner has goods", ctx => false)
                    .End()
                    .Action("Wait")
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            domain.FindPlan(ctx, out _);

            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(3, trace.Count);
            AssertRecord(trace[0], 2, "Choose", "Barter", TraceOutcome.Refused, "Partner has goods");
            AssertRecord(trace[1], 2, "Choose", "Wait", TraceOutcome.Succeeded, null);
            AssertRecord(trace[2], 1, "Test", "Choose", TraceOutcome.Succeeded, null);

            var barter = (IPrimitiveTask) trace[0].Method;
            Assert.AreSame(barter.Conditions[0], trace[0].RefusedCondition);
        }

        /// <summary>
        /// A sequence fails at its second step. The trace shows the step that the sequence took,
        /// the refused step, the failed sequence, and the method that the selector took next.
        /// </summary>
        [TestMethod]
        public void FailedSequenceRecordsEachDepth()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Sequence("Trade")
                        .Action("Pick partner")
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

            domain.FindPlan(ctx, out _);

            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(5, trace.Count);
            AssertRecord(trace[0], 3, "Trade", "Pick partner", TraceOutcome.Succeeded, null);
            AssertRecord(trace[1], 3, "Trade", "Barter", TraceOutcome.Refused, "Partner has goods");
            AssertRecord(trace[2], 2, "Choose", "Trade", TraceOutcome.Failed, null);
            AssertRecord(trace[3], 2, "Choose", "Wait", TraceOutcome.Succeeded, null);
            AssertRecord(trace[4], 1, "Test", "Choose", TraceOutcome.Succeeded, null);
        }

        /// <summary>
        /// The running plan took method 0. Method 0 is refused, and method 1 cannot beat the
        /// method traversal record of the running plan, so the selector rejects it.
        /// </summary>
        [TestMethod]
        public void MethodThatCannotBeatRunningPlanRecordsRejected()
        {
            var ctx = NewContext();
            var task = new Selector() { Name = "Choose" };
            var first = new PrimitiveTask() { Name = "Barter", Parent = task };
            first.AddCondition(new FuncCondition<MyContext>("Partner has goods", context => false));
            task.AddSubtask(first);
            task.AddSubtask(new PrimitiveTask() { Name = "Wait", Parent = task });
            ctx.LastMTR.Add(0);

            var status = task.Decompose(ctx, 0, out _);

            Assert.AreEqual(DecompositionStatus.Rejected, status);
            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(2, trace.Count);
            AssertRecord(trace[0], 1, "Choose", "Barter", TraceOutcome.Refused, "Partner has goods");
            AssertRecord(trace[1], 1, "Choose", "Wait", TraceOutcome.Rejected, null);
        }

        /// <summary>
        /// A context with LogDecomposition false gets no trace list and no records.
        /// </summary>
        [TestMethod]
        public void NoTraceWhenLogDecompositionIsFalse()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Action("Wait")
                .End()
                .Build();
            var ctx = new MyContext();
            ctx.Init();

            domain.FindPlan(ctx, out _);

            Assert.IsNull(ctx.DecompositionTrace);
        }
    }
}
