using AwesomeAssertions;
using NUnit.Framework;
using RomForge.Core.Services;
using RomForge.UI.ViewModels;

namespace RomForge.UI.UnitTests.ViewModels;

[TestOf(typeof(ImageDownloadWindowVM))]
public sealed class ImageDownloadWindowVMTests
{
    [Test]
    public void Constructor_StartsRunningWithEmptyLog()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.LogText.Should().BeEmpty();
        vm.IsRunning.Should().BeTrue();
        vm.IsComplete.Should().BeFalse();
        vm.CloseButtonText.Should().Be("Cancel");
    }

    [Test]
    public void IsIndeterminate_TrueBeforeTotalKnown_FalseOnceTotalSet()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.IsIndeterminate.Should().BeTrue();

        vm.Report(new ImageSyncProgress(1, 5, "1-500/1a.png", true));

        vm.IsIndeterminate.Should().BeFalse();
    }

    [Test]
    public void Report_UpdatesCountAndAppendsLine()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Report(new ImageSyncProgress(2, 5, "1-500/2a.png", true));

        vm.Current.Should().Be(2);
        vm.Total.Should().Be(5);
        vm.CountText.Should().Be("2 of 5 downloaded");
        vm.LogText.Should().Contain("1-500/2a.png");
    }

    [Test]
    public void Report_FailedImage_LogsFailureMarker()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Report(new ImageSyncProgress(1, 1, "1-500/1b.png", false));

        vm.LogText.Should().Contain("✗").And.Contain("1-500/1b.png");
    }

    [Test]
    public void Finish_WithDownloads_MarksCompleteAndSwitchesButtonToClose()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Finish(new ImageSyncSummary(3, 0, 3));

        vm.IsComplete.Should().BeTrue();
        vm.IsRunning.Should().BeFalse();
        vm.CloseButtonText.Should().Be("Close");
        vm.LogText.Should().Contain("Downloaded 3 of 3");
    }

    [Test]
    public void Finish_NothingMissing_ReportsEverythingPresent()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Finish(new ImageSyncSummary(0, 0, 0));

        vm.LogText.Should().Contain("No missing images");
    }

    [Test]
    public void Finish_WithFailures_ReportsFailureCount()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Finish(new ImageSyncSummary(2, 1, 3));

        vm.LogText.Should().Contain("1 failed");
    }

    [Test]
    public void Cancelled_MarksCompleteAndLogsCancellation()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.Cancelled();

        vm.IsComplete.Should().BeTrue();
        vm.LogText.Should().Contain("Cancelled");
    }

    [Test]
    public void CloseOrCancel_WhileRunning_CancelsToken()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();

        vm.CloseOrCancelCommand.Execute(null);

        vm.CancellationToken.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void CloseOrCancel_WhenComplete_InvokesRequestClose()
    {
        using ImageDownloadWindowVM vm = new ImageDownloadWindowVM();
        bool closed = false;
        vm.RequestClose = () => closed = true;
        vm.Finish(new ImageSyncSummary(0, 0, 0));

        vm.CloseOrCancelCommand.Execute(null);

        closed.Should().BeTrue();
    }
}
