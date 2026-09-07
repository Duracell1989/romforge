using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.IO;

namespace RomForge.Core.UnitTests.IO
{
    [TestOf(typeof(WorkingSetBudgetGate))]
    public sealed class WorkingSetBudgetGateTests
    {
        [Test]
        public void Constructor_NonPositiveBudget_Throws()
        {
            Action act = () => _ = new WorkingSetBudgetGate(0);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Test]
        public void BudgetBytes_ReturnsConstructorValue()
        {
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(42);

            gate.BudgetBytes.Should().Be(42);
        }

        [Test]
        public void CreateDefault_ReturnsPositiveBudget()
        {
            // GC.GetGCMemoryInfo().TotalAvailableMemoryBytes varies by machine and CI runner, so
            // this only pins the invariant the constructor itself already enforces: a real budget
            // is always positive, never zero or negative regardless of what the runtime reports.
            WorkingSetBudgetGate gate = WorkingSetBudgetGate.CreateDefault();

            gate.BudgetBytes.Should().BePositive();
        }

        [Test]
        public async Task AcquireAsync_WithinBudget_AdmitsImmediately()
        {
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(20);

            IDisposable lease = await gate.AcquireAsync(10, CancellationToken.None);

            lease.Should().NotBeNull();
        }

        [Test]
        public async Task AcquireAsync_WouldExceedBudget_WaitsUntilLeaseIsReleased()
        {
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(20);
            IDisposable first = await gate.AcquireAsync(10, CancellationToken.None);
            IDisposable second = await gate.AcquireAsync(10, CancellationToken.None);

            // 30 > 20, so the third job must not be admitted while both leases are held.
            Task<IDisposable> third = gate.AcquireAsync(10, CancellationToken.None);
            await Task.WhenAny(third, Task.Delay(100));
            third.IsCompleted.Should().BeFalse("the budget is full while two 10-cost leases are held");

            // Releasing one lease frees enough budget for the waiting job.
            first.Dispose();
            IDisposable admitted = await third;
            admitted.Should().NotBeNull();

            second.Dispose();
            admitted.Dispose();
        }

        [Test]
        public async Task AcquireAsync_JobLargerThanEntireBudget_RunsAloneWhenNothingInFlight()
        {
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(5);

            // A single job larger than the whole budget must still make progress (alone), or a
            // 3DS ROM whose estimate exceeds the budget would deadlock the batch.
            IDisposable oversized = await gate.AcquireAsync(10, CancellationToken.None);
            oversized.Should().NotBeNull();

            // But nothing else may join it while it is in flight.
            Task<IDisposable> second = gate.AcquireAsync(1, CancellationToken.None);
            await Task.WhenAny(second, Task.Delay(100));
            second.IsCompleted.Should().BeFalse();

            oversized.Dispose();
            (await second).Should().NotBeNull();
        }

        [Test]
        public async Task AcquireAsync_FiveJobsAtHalfBudgetEach_NeverExceedsTwoConcurrent()
        {
            // The core regression: five 3DS-sized jobs whose working set is half the budget each
            // must run at most two at a time, no matter how many the outer count throttle allows.
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(20);
            int active = 0;
            int maxActive = 0;
            object guard = new object();

            async Task RunJob()
            {
                using IDisposable lease = await gate.AcquireAsync(10, CancellationToken.None);
                lock (guard)
                {
                    active++;
                    maxActive = Math.Max(maxActive, active);
                }
                await Task.Delay(30);
                lock (guard)
                    active--;
            }

            await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RunJob()));

            maxActive.Should().Be(2, "20 / 10 == 2 jobs fit the budget at once");
        }

        [Test]
        public async Task AcquireAsync_NewcomerThatWouldFit_DoesNotOvertakeAnAlreadyWaitingJob()
        {
            // Fairness: admission is FIFO, so a cheap newcomer may not barge past an expensive job
            // already in the queue. Without this, Release woke every waiter outside the lock and a
            // fresh caller could take the lock first — on a mixed DAT (GBA ~0.4 GB/job vs 3DS
            // ~2.9 GB/job) the expensive waiter is overtaken every cycle and only makes progress
            // when the batch happens to run out of cheap jobs.
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(10);
            IDisposable held = await gate.AcquireAsync(8, CancellationToken.None);

            // 8 + 5 > 10, so the expensive job queues.
            Task<IDisposable> expensive = gate.AcquireAsync(5, CancellationToken.None);
            await Task.WhenAny(expensive, Task.Delay(100));
            expensive.IsCompleted.Should().BeFalse();

            // 8 + 1 <= 10, so this newcomer *would* fit — but it arrived second and must wait.
            Task<IDisposable> cheap = gate.AcquireAsync(1, CancellationToken.None);
            await Task.WhenAny(cheap, Task.Delay(100));
            cheap.IsCompleted.Should().BeFalse("a newcomer must not overtake a queued job");

            // Releasing frees the whole budget: the expensive job goes first, then the cheap one
            // (5 + 1 <= 10, so both fit once the queue is drained in order).
            held.Dispose();
            IDisposable first = await expensive;
            IDisposable second = await cheap;

            first.Dispose();
            second.Dispose();
        }

        [Test]
        public async Task AcquireAsync_WaiterCancelledWhileQueued_DoesNotStrandTheJobsBehindIt()
        {
            // A cancelled waiter must be removed from the FIFO queue and must not leave its cost
            // charged against the budget, or everything behind it blocks forever.
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(10);
            IDisposable held = await gate.AcquireAsync(8, CancellationToken.None);
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task<IDisposable> cancelled = gate.AcquireAsync(5, cts.Token);
            await Task.WhenAny(cancelled, Task.Delay(100));
            Task<IDisposable> behind = gate.AcquireAsync(5, CancellationToken.None);

            await cts.CancelAsync();
            Func<Task> act = async () => await cancelled;
            await act.Should().ThrowAsync<OperationCanceledException>();

            held.Dispose();
            IDisposable admitted = await behind;
            admitted.Should().NotBeNull();
            admitted.Dispose();
        }

        [Test]
        public async Task AcquireAsync_Cancelled_ThrowsOperationCanceled()
        {
            WorkingSetBudgetGate gate = new WorkingSetBudgetGate(10);
            IDisposable full = await gate.AcquireAsync(10, CancellationToken.None);
            using CancellationTokenSource cts = new CancellationTokenSource();

            Task<IDisposable> waiting = gate.AcquireAsync(5, cts.Token);
            await cts.CancelAsync();

            Func<Task> act = async () => await waiting;
            await act.Should().ThrowAsync<OperationCanceledException>();

            full.Dispose();
        }
    }
}
