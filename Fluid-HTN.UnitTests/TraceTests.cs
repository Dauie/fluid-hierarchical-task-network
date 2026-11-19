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

        private static void AssertHasRecord(List<DecompositionTraceRecord> trace, string task, string method,
            TraceOutcome outcome)
        {
            foreach (var record in trace)
            {
                if (record.Task?.Name == task && record.Method.Name == method && record.Outcome == outcome)
                {
                    return;
                }
            }

            Assert.Fail($"No record {task} > {method}: {outcome}. Records: {string.Join("; ", trace)}");
        }

        /// <summary>
        /// The condition of a compound method refuses it. This is the usual form of a method,
        /// and a reader explains a plan from the condition in this record.
        /// </summary>
        [TestMethod]
        public void RefusedCompoundMethodRecordsItsCondition()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Sequence("Trade")
                        .Condition("Has partner", ctx => false)
                        .Action("Barter")
                        .End()
                    .End()
                    .Action("Wait")
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            domain.FindPlan(ctx, out _);

            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(3, trace.Count);
            AssertRecord(trace[0], 2, "Choose", "Trade", TraceOutcome.Refused, "Has partner");
            AssertRecord(trace[1], 2, "Choose", "Wait", TraceOutcome.Succeeded, null);
            AssertRecord(trace[2], 1, "Test", "Choose", TraceOutcome.Succeeded, null);
        }

        /// <summary>
        /// A sequence has a compound step and then a pause. The trace records the compound step
        /// and the pause of the sequence.
        /// </summary>
        [TestMethod]
        public void SequenceRecordsCompoundStepAndPause()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Sequence("Trade")
                    .Select("Pick")
                        .Action("Pick partner")
                        .End()
                    .End()
                    .PausePlan()
                    .Action("Barter")
                    .End()
                .End()
                .Build();
            var ctx = NewContext();

            domain.FindPlan(ctx, out _);

            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(4, trace.Count);
            AssertRecord(trace[0], 3, "Pick", "Pick partner", TraceOutcome.Succeeded, null);
            AssertRecord(trace[1], 2, "Trade", "Pick", TraceOutcome.Succeeded, null);
            AssertRecord(trace[2], 2, "Trade", "Pause Plan", TraceOutcome.Partial, null);
            AssertRecord(trace[3], 1, "Test", "Trade", TraceOutcome.Partial, null);
        }

        /// <summary>
        /// A selector and a sequence with no subtasks are refused with no condition.
        /// </summary>
        [TestMethod]
        public void EmptyCompoundMethodsRecordRefusedWithNoCondition()
        {
            var ctx = NewContext();
            var choose = new Selector() { Name = "Choose" };
            choose.AddSubtask(new Selector() { Name = "Empty selector", Parent = choose });
            choose.AddSubtask(new Sequence() { Name = "Empty sequence", Parent = choose });
            choose.AddSubtask(new PrimitiveTask() { Name = "Wait", Parent = choose });

            choose.Decompose(ctx, 0, out _);

            var trace = ctx.DecompositionTrace;
            Assert.AreEqual(3, trace.Count);
            AssertRecord(trace[0], 1, "Choose", "Empty selector", TraceOutcome.Refused, null);
            AssertRecord(trace[1], 1, "Choose", "Empty sequence", TraceOutcome.Refused, null);
            AssertRecord(trace[2], 1, "Choose", "Wait", TraceOutcome.Succeeded, null);
        }

        /// <summary>
        /// A slot with no domain is refused with no condition. A slot with a domain
        /// is recorded as a method of its selector or its sequence.
        /// </summary>
        [TestMethod]
        public void SlotRecordsRefusedThenSucceeded()
        {
            var domain = new DomainBuilder<MyContext>("Test")
                .Select("Choose")
                    .Slot(1)
                    .Sequence("Trade")
                        .Slot(2)
                    .End()
                .End()
                .Build();
            var work = new DomainBuilder<MyContext>("Sub")
                .Action("Work")
                .End()
                .Build();
            var ctx = NewContext();

            Assert.IsTrue(domain.TrySetSlotDomain(2, work));
            domain.FindPlan(ctx, out _);

            AssertRecord(ctx.DecompositionTrace[0], 2, "Choose", "Slot 1", TraceOutcome.Refused, null);
            AssertHasRecord(ctx.DecompositionTrace, "Trade", "Slot 2", TraceOutcome.Succeeded);

            var ctx2 = NewContext();
            Assert.IsTrue(domain.TrySetSlotDomain(1, work));
            domain.FindPlan(ctx2, out _);

            AssertHasRecord(ctx2.DecompositionTrace, "Choose", "Slot 1", TraceOutcome.Succeeded);
        }
    }
}
