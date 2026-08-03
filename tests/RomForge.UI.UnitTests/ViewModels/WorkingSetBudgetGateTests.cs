using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NUnit.Framework;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels
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
            third
                .IsCompleted.Should()
                .BeFalse("the budget is full while two 10-cost leases are held");

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
