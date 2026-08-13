using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using RomForge.UI.Services;
using Serilog;

namespace RomForge.UI.ViewModels
{
    /// <summary>
    /// Runs a sequential batch of per-item work behind a modal progress window, collecting per-item
    /// error messages without aborting the batch. Owns the skeleton shared by every sequential bulk
    /// ROM operation (rename, trim, move): the empty-target guard, the progress-window lifecycle, the
    /// per-item progress updates, cooperative cancellation, error aggregation, the completion log line
    /// and the failure notification. The caller supplies only the per-item work and the labels.
    /// </summary>
    internal sealed class BatchProgressRunner
    {
        private readonly IUserNotifier _notifier;
        private readonly ILogger _logger;

        public BatchProgressRunner(IUserNotifier notifier, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(notifier);
            ArgumentNullException.ThrowIfNull(logger);
            _notifier = notifier;
            _logger = logger;
        }

        /// <summary>
        /// Runs <paramref name="operation"/> to completion and returns the number of items that
        /// succeeded. A no-op returning zero when there are no targets.
        /// </summary>
        public async Task<int> RunAsync<T>(BatchProgressOperation<T> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);

            if (operation.Targets.Count == 0)
                return 0;

            var progress = new ProgressWindowVM(operation.Targets.Count, operation.IsCancellable);
            var operationTask = RunCoreAsync(operation, progress);
            await _notifier.ShowProgressAsync(operation.Title, progress, operationTask);

            var errors = await operationTask;
            var succeeded = operation.Targets.Count - errors.Count;
            _logger.Information("{Label}: {Succeeded}/{Total} {Verb}", operation.LogLabel, succeeded, operation.Targets.Count, operation.CompletedVerb);

            if (errors.Count > 0)
            {
                await _notifier.NotifyErrorAsync($"{operation.FailureLabel} failed for {errors.Count} file(s):\n{string.Join("\n", errors)}");
            }

            return succeeded;
        }

        private async Task<List<string>> RunCoreAsync<T>(BatchProgressOperation<T> operation, ProgressWindowVM progress)
        {
            operation.BusyFlag?.Invoke(true);
            var errors = new List<string>();

            try
            {
                // Yield so this task is still pending when the caller hands it to the progress
                // window. A worker that completes synchronously (e.g. a File.Move-backed rename)
                // would otherwise run the whole batch here — before the window is shown — leaving
                // the window to open onto an already-finished task and close instantly.
                await Task.Yield();

                for (var i = 0; i < operation.Targets.Count; i++)
                {
                    if (progress.CancellationToken.IsCancellationRequested)
                        break;

                    var item = operation.Targets[i];
                    progress.Current = i + 1;
                    progress.CurrentFile = Path.GetFileName(operation.FileName(item));
                    if (operation.BumpOverallProgress)
                        progress.Progress = (i + 1) * 100 / operation.Targets.Count;

                    var error = await operation.ProcessAsync(item, progress);
                    if (error is not null)
                        errors.Add(error);
                }
            }
            catch (OperationCanceledException ex)
            {
                _logger.Information(ex, "{Label} cancelled after {Completed} of {Total}", operation.LogLabel, progress.Current, operation.Targets.Count);
            }
            finally
            {
                operation.BusyFlag?.Invoke(false);
            }

            return errors;
        }
    }

    /// <summary>
    /// Describes one sequential bulk operation for <see cref="BatchProgressRunner"/>.
    /// </summary>
    /// <typeparam name="T">The item type processed by the batch (e.g. a game row or a scanned ROM).</typeparam>
    internal sealed record BatchProgressOperation<T>
    {
        /// <summary>Progress-window title, e.g. "Renaming ROMs".</summary>
        public required string Title { get; init; }

        /// <summary>Log-line label, e.g. "Rename all".</summary>
        public required string LogLabel { get; init; }

        /// <summary>Noun used in the failure notification, e.g. "Rename" → "Rename failed for N file(s)".</summary>
        public required string FailureLabel { get; init; }

        /// <summary>Past-tense verb for the completion log, e.g. "succeeded" or "moved".</summary>
        public required string CompletedVerb { get; init; }

        public required IReadOnlyList<T> Targets { get; init; }

        /// <summary>Selects the file path used for the progress window's current-file label.</summary>
        public required Func<T, string> FileName { get; init; }

        /// <summary>Processes one item, returning an error message on failure or null on success.</summary>
        public required Func<T, ProgressWindowVM, Task<string?>> ProcessAsync { get; init; }

        public bool IsCancellable { get; init; }

        /// <summary>
        /// When true, the runner sets the overall percentage after each item. Leave false for
        /// operations that publish sub-item progress themselves (e.g. compression callbacks).
        /// </summary>
        public bool BumpOverallProgress { get; init; }

        /// <summary>Optional busy-flag toggle, set true for the run and reset in a finally block.</summary>
        public Action<bool>? BusyFlag { get; init; }
    }
}
