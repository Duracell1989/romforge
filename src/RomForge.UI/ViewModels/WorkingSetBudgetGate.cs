using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RomForge.UI.ViewModels
{
    /// <summary>
    /// Admits concurrent work up to a total estimated-memory budget rather than a fixed job count.
    /// A job is admitted once the sum of the in-flight costs plus its own cost fits the budget; a
    /// job whose cost exceeds the whole budget still runs, but only ever alone (so it can never
    /// deadlock). Callers dispose the returned lease to release their share of the budget. Used to
    /// keep large re-archive jobs — whose LZMA2 working set scales with ROM size — from running
    /// parallel enough to exhaust physical memory and hang the machine.
    /// </summary>
    internal sealed class WorkingSetBudgetGate
    {
        private readonly long _budgetBytes;
        private readonly object _lock = new object();
        private readonly List<TaskCompletionSource> _waiters = new List<TaskCompletionSource>();
        private long _inFlightBytes;

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
        /// Waits until <paramref name="costBytes"/> fits the remaining budget, then reserves it and
        /// returns a lease that releases the reservation on dispose.
        /// </summary>
        /// <exception cref="OperationCanceledException">
        /// Thrown when <paramref name="cancellationToken"/> is cancelled while waiting.
        /// </exception>
        public async Task<IDisposable> AcquireAsync(
            long costBytes,
            CancellationToken cancellationToken
        )
        {
            long cost = Math.Max(0, costBytes);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                TaskCompletionSource waiter;
                lock (_lock)
                {
                    // Admit when the job fits the remaining budget, or when nothing else is in
                    // flight — the latter lets a job larger than the whole budget make progress
                    // alone instead of deadlocking.
                    if (_inFlightBytes == 0 || _inFlightBytes + cost <= _budgetBytes)
                    {
                        _inFlightBytes += cost;
                        return new Lease(this, cost);
                    }

                    waiter = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
                    _waiters.Add(waiter);
                }

                using (cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken)))
                    await waiter.Task.ConfigureAwait(false);
            }
        }

        private void Release(long cost)
        {
            List<TaskCompletionSource> toWake;
            lock (_lock)
            {
                _inFlightBytes -= cost;
                // Wake every waiter to re-check. The concurrency here is bounded by the outer count
                // throttle, so the herd is tiny; each waiter that still cannot fit simply re-queues.
                toWake = new List<TaskCompletionSource>(_waiters);
                _waiters.Clear();
            }

            foreach (TaskCompletionSource waiter in toWake)
                waiter.TrySetResult();
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
