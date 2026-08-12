using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.Core.IO
{
    /// <summary>
    /// Admits concurrent work up to a total estimated-memory budget rather than a fixed job count.
    /// A job is admitted once the sum of the in-flight costs plus its own cost fits the budget; a
    /// job whose cost exceeds the whole budget still runs, but only ever alone (so it can never
    /// deadlock). Callers dispose the returned lease to release their share of the budget. Used to
    /// keep large re-archive/trim jobs — whose LZMA2 working set scales with dictionary size — from
    /// running parallel enough to exhaust physical memory and hang the machine.
    /// </summary>
    /// <remarks>
    /// Registered as a single DI instance shared across every compress-based operation (batch
    /// re-archive, single re-archive, batch trim, single trim). Per-operation instances would each
    /// get their own full budget, so a re-archive and a trim running at the same time could
    /// together exceed physical memory even though each individually stays within its own budget.
    /// </remarks>
    public sealed class WorkingSetBudgetGate
    {
        // Fraction of TOTAL PHYSICAL memory the concurrent compress-based operations' working sets
        // may occupy together. Kept well under 1.0 so the OS, the app and file-cache pressure keep
        // headroom — exceeding physical memory during a large-ROM (3DS) batch previously thrashed
        // swap hard enough to trip the kernel watchdog and hang the machine.
        private const double DefaultBudgetFraction = 0.6;

        private readonly long _budgetBytes;

        // System.Threading.Lock (.NET 9+) rather than a plain object: the lock statement binds to
        // Lock.EnterScope, which is cheaper under contention than Monitor on an object and makes it
        // a compile error to lock on something that was never meant to be a lock. Nothing here needs
        // Monitor.Wait/Pulse, which is the only capability the dedicated type gives up.
        private readonly Lock _lock = new Lock();
        private readonly LinkedList<Waiter> _waiters = new LinkedList<Waiter>();
        private long _inFlightBytes;

        /// <summary>
        /// Creates a gate with a fixed budget in bytes.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="budgetBytes"/> is not positive.
        /// </exception>
        public WorkingSetBudgetGate(long budgetBytes)
        {
            if (budgetBytes <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(budgetBytes),
                    budgetBytes,
                    "Budget must be positive."
                );
            _budgetBytes = budgetBytes;
        }

        /// <summary>
        /// The total budget this gate admits against. Compressors use this to size their dictionary
        /// so a single job's estimated working set never exceeds it — the reason the "runs alone
        /// when nothing else fits" fallback in <see cref="AcquireAsync"/> should not normally trigger.
        /// </summary>
        public long BudgetBytes => _budgetBytes;

        /// <summary>
        /// Creates a gate budgeted as a fixed fraction of this machine's total physical memory.
        /// </summary>
        /// <remarks>
        /// This is a deliberately <em>static</em> policy, not a live reading. Measured 2026-08-11:
        /// <c>GC.GetGCMemoryInfo().TotalAvailableMemoryBytes</c> reports the memory available to the
        /// GC — total physical RAM, or the container / <c>GCHeapHardLimit</c> ceiling — and does
        /// <em>not</em> fall as other processes consume memory (it read a flat 48 GiB on a 48 GB Mac
        /// before and after another 2 GiB was allocated and touched; only <c>MemoryLoadBytes</c>
        /// moved). An earlier revision re-invoked this per read and documented it as adapting to
        /// current conditions; it was re-reading a constant. If genuine adaptivity is ever wanted it
        /// has to come from <c>TotalAvailableMemoryBytes - MemoryLoadBytes</c>, bearing in mind that
        /// <c>MemoryLoadBytes</c> is a snapshot from the last GC and reads zero before the first one.
        /// </remarks>
        public static WorkingSetBudgetGate CreateDefault() =>
            new WorkingSetBudgetGate(ComputeDefaultBudgetBytes());

        private static long ComputeDefaultBudgetBytes() =>
            Math.Max(
                1,
                (long)(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes * DefaultBudgetFraction)
            );

        /// <summary>
        /// Waits until <paramref name="costBytes"/> fits the remaining budget, then reserves it and
        /// returns a lease that releases the reservation on dispose.
        /// </summary>
        /// <remarks>
        /// Admission is FIFO: a caller that arrives while others are queued always joins the back of
        /// the queue, even when its cost would fit right now. Allowing newcomers to overtake lets a
        /// stream of cheap jobs starve an expensive one indefinitely — on a mixed DAT the 3DS job
        /// waits while GBA jobs cycle through ahead of it.
        /// </remarks>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled while waiting.
        /// </exception>
        public async Task<IDisposable> AcquireAsync(
            long costBytes,
            CancellationToken cancellationToken
        )
        {
            long cost = Math.Max(0, costBytes);
            cancellationToken.ThrowIfCancellationRequested();

            LinkedListNode<Waiter> node;
            lock (_lock)
            {
                if (_waiters.Count == 0 && FitsLocked(cost))
                {
                    _inFlightBytes += cost;
                    return new Lease(this, cost);
                }

                node = _waiters.AddLast(new Waiter(cost));
            }

            try
            {
                using (
                    cancellationToken.Register(() =>
                        node.Value.Admitted.TrySetCanceled(cancellationToken)
                    )
                )
                    await node.Value.Admitted.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Drop out of the queue and let whoever is behind take the freed place — a
                // cancelled waiter left in the list would block the whole queue behind it.
                List<Waiter> admitted;
                lock (_lock)
                {
                    if (node.List is not null)
                        _waiters.Remove(node);
                    admitted = DrainLocked();
                }

                CompleteAll(admitted);
                throw;
            }

            // The releasing thread charged this job's cost before handing it the slot, so by the
            // time the wait completes the reservation is already made.
            return new Lease(this, cost);
        }

        // Admit when the job fits the remaining budget, or when nothing else is in flight — the
        // latter lets a job larger than the whole budget make progress alone instead of deadlocking.
        private bool FitsLocked(long cost) =>
            _inFlightBytes == 0 || _inFlightBytes + cost <= _budgetBytes;

        // Selects, strictly from the front, the queued jobs that now fit, and charges their cost
        // here on the releasing thread rather than letting each waiter re-check after it wakes.
        // That is what makes the hand-off atomic: there is no gap between "waiter chosen" and
        // "waiter resumes" for a newcomer to slip into. Callers must be holding the lock, and must
        // hand the returned waiters to CompleteAll *outside* it — completing them here would let
        // CompleteAll's refund path re-enter the lock while it is still held.
        private List<Waiter> DrainLocked()
        {
            List<Waiter> admitted = new List<Waiter>();
            while (_waiters.First is not null)
            {
                Waiter head = _waiters.First.Value;
                if (!FitsLocked(head.Cost))
                    break;

                _waiters.RemoveFirst();
                admitted.Add(head);
                _inFlightBytes += head.Cost;
            }

            return admitted;
        }

        // Hands each drained waiter its slot. A waiter that lost the race to its own cancellation
        // will never run, so its already-charged reservation is refunded and the freed budget
        // re-drained — iteratively, since the refund can admit further waiters that may in turn
        // have been cancelled.
        private void CompleteAll(List<Waiter> admitted)
        {
            List<Waiter> pending = admitted;
            while (pending.Count > 0)
            {
                long refund = 0;
                foreach (Waiter waiter in pending)
                {
                    bool handedOver = waiter.Admitted.TrySetResult();
                    refund += handedOver ? 0 : waiter.Cost;
                }

                if (refund == 0)
                    return;

                lock (_lock)
                {
                    _inFlightBytes -= refund;
                    pending = DrainLocked();
                }
            }
        }

        private void Release(long cost)
        {
            List<Waiter> admitted;
            lock (_lock)
            {
                _inFlightBytes -= cost;
                admitted = DrainLocked();
            }

            CompleteAll(admitted);
        }

        private sealed class Waiter
        {
            public Waiter(long cost)
            {
                Cost = cost;
                Admitted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            }

            public long Cost { get; }
            public TaskCompletionSource Admitted { get; }
        }

        private sealed class Lease : IDisposable
        {
            private readonly WorkingSetBudgetGate _gate;
            private readonly long _cost;
            private bool _disposed;

            public Lease(WorkingSetBudgetGate gate, long cost)
            {
                _gate = gate;
                _cost = cost;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _gate.Release(_cost);
            }
        }
    }
}
