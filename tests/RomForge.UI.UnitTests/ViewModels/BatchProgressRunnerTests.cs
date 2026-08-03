using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Moq;
using NUnit.Framework;
using RomForge.UI.Services;
using RomForge.UI.ViewModels;
using Serilog;

namespace RomForge.UI.UnitTests.ViewModels
{
    [TestOf(typeof(BatchProgressRunner))]
    public sealed class BatchProgressRunnerTests
    {
        private Mock<IUserNotifier> _notifier = null!;
        private BatchProgressRunner _runner = null!;

        [SetUp]
        public void SetUp()
        {
            _notifier = new Mock<IUserNotifier>();
            // Run the operation task the runner hands to the progress window, as the real notifier does.
            _notifier
                .Setup(n =>
                    n.ShowProgressAsync(
                        It.IsAny<string>(),
                        It.IsAny<ProgressWindowVM>(),
                        It.IsAny<Task>()
                    )
                )
                .Returns<string, ProgressWindowVM, Task>((_, _, task) => task);

            ILogger logger = new LoggerConfiguration().CreateLogger();
            _runner = new BatchProgressRunner(_notifier.Object, logger);
        }

        private static BatchProgressOperation<string> Operation(
            IReadOnlyList<string> targets,
            Func<string, ProgressWindowVM, Task<string?>> process,
            bool cancellable = false,
            bool bumpProgress = false,
            Action<bool>? busyFlag = null
        ) =>
            new BatchProgressOperation<string>
            {
                Title = "Working",
                LogLabel = "Work all",
                FailureLabel = "Work",
                CompletedVerb = "done",
                Targets = targets,
                FileName = t => t,
                ProcessAsync = process,
                IsCancellable = cancellable,
                BumpOverallProgress = bumpProgress,
                BusyFlag = busyFlag,
            };

        [Test]
        public async Task RunAsync_NoTargets_IsNoOpAndReturnsZero()
        {
            int succeeded = await _runner.RunAsync(
                Operation([], (_, _) => Task.FromResult<string?>(null))
            );

            succeeded.Should().Be(0);
            _notifier.Verify(
                n =>
                    n.ShowProgressAsync(
                        It.IsAny<string>(),
                        It.IsAny<ProgressWindowVM>(),
                        It.IsAny<Task>()
                    ),
                Times.Never
            );
            _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task RunAsync_AllSucceed_ProcessesEveryItemAndReturnsCount()
        {
            List<string> processed = new List<string>();

            int succeeded = await _runner.RunAsync(
                Operation(
                    ["a", "b", "c"],
                    (t, _) =>
                    {
                        processed.Add(t);
                        return Task.FromResult<string?>(null);
                    }
                )
            );

            succeeded.Should().Be(3);
            processed.Should().Equal("a", "b", "c");
            _notifier.Verify(n => n.NotifyErrorAsync(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public async Task RunAsync_SomeFail_AggregatesErrorsAndReturnsSucceededCount()
        {
            int succeeded = await _runner.RunAsync(
                Operation(
                    ["ok", "bad", "ok2"],
                    (t, _) => Task.FromResult<string?>(t == "bad" ? "boom" : null)
                )
            );

            succeeded.Should().Be(2);
            _notifier.Verify(
                n =>
                    n.NotifyErrorAsync(
                        It.Is<string>(m => m.Contains("1 file(s)") && m.Contains("boom"))
                    ),
                Times.Once
            );
        }

        [Test]
        public async Task RunAsync_BumpOverallProgress_SetsPercentPerItem()
        {
            int lastProgress = -1;

            await _runner.RunAsync(
                Operation(
                    ["a", "b", "c", "d"],
                    (_, progress) =>
                    {
                        lastProgress = progress.Progress;
                        return Task.FromResult<string?>(null);
                    },
                    bumpProgress: true
                )
            );

            // After the 4th of 4 items: 4 * 100 / 4 == 100.
            lastProgress.Should().Be(100);
        }

        [Test]
        public async Task RunAsync_NoBumpOverallProgress_LeavesProgressForTheWorker()
        {
            int observed = -1;

            await _runner.RunAsync(
                Operation(
                    ["a", "b"],
                    (_, progress) =>
                    {
                        observed = progress.Progress;
                        return Task.FromResult<string?>(null);
                    },
                    bumpProgress: false
                )
            );

            observed.Should().Be(0);
        }

        [Test]
        public async Task RunAsync_BusyFlag_SetTrueDuringRunAndResetAfter()
        {
            List<bool> transitions = new List<bool>();
            bool duringRun = false;

            await _runner.RunAsync(
                Operation(
                    ["a"],
                    (_, _) =>
                    {
                        duringRun = transitions.Count > 0 && transitions[^1];
                        return Task.FromResult<string?>(null);
                    },
                    busyFlag: transitions.Add
                )
            );

            duringRun.Should().BeTrue("the busy flag must be set before items are processed");
            transitions.Should().Equal(true, false);
        }

        [Test]
        public async Task RunAsync_Cancelled_StopsProcessingRemainingItems()
        {
            ProgressWindowVM? captured = null;
            List<string> processed = new List<string>();

            int succeeded = await _runner.RunAsync(
                Operation(
                    ["a", "b", "c"],
                    (t, progress) =>
                    {
                        captured ??= progress;
                        processed.Add(t);
                        // Cancel after the first item; the loop must not process "c".
                        captured.CancelCommand.Execute(null);
                        return Task.FromResult<string?>(null);
                    },
                    cancellable: true
                )
            );

            processed.Should().Equal("a");
            succeeded
                .Should()
                .Be(3, "cancellation is not a failure — only processed items count against errors");
        }

        [Test]
        public async Task RunAsync_WithSynchronousWorker_ShowsProgressBeforeProcessingAnyItem()
        {
            // A worker that completes synchronously (e.g. a File.Move-backed rename) must not run
            // the whole batch before the progress window is shown. Otherwise the window is handed
            // an already-completed task and closes immediately — the work happens with no UI on
            // screen, then the popup only flashes.
            int processed = 0;
            int processedWhenShown = -1;
            _notifier
                .Setup(n =>
                    n.ShowProgressAsync(
                        It.IsAny<string>(),
                        It.IsAny<ProgressWindowVM>(),
                        It.IsAny<Task>()
                    )
                )
                .Returns<string, ProgressWindowVM, Task>(
                    (_, _, task) =>
                    {
                        processedWhenShown = processed;
                        return task;
                    }
                );

            await _runner.RunAsync(
                Operation(
                    ["a", "b", "c"],
                    (_, _) =>
                    {
                        processed++;
                        return Task.FromResult<string?>(null);
                    }
                )
            );

            processedWhenShown
                .Should()
                .Be(0, "the progress window must be shown before any item is processed");
            processed.Should().Be(3);
        }

        [Test]
        public async Task RunAsync_BusyFlag_ResetEvenWhenWorkerThrowsOperationCanceled()
        {
            List<bool> transitions = new List<bool>();

            await _runner.RunAsync(
                Operation(
                    ["a", "b"],
                    (_, _) => throw new OperationCanceledException(),
                    cancellable: true,
                    busyFlag: transitions.Add
                )
            );

            transitions.Should().Equal(true, false);
        }
    }
}
