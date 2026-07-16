using System;
using System.Text;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomForge.Core.Services;

namespace RomForge.UI.ViewModels;

/// <summary>
/// Backs the dedicated image-download dialog shown during a DAT update. It appends a running
/// log of each image as it is fetched and tracks an <c>X of Y downloaded</c> counter, staying
/// open after completion so the result can be read.
/// </summary>
public sealed partial class ImageDownloadWindowVM : VMBase, IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly StringBuilder _log;

    public CancellationToken CancellationToken => _cts.Token;

    /// <summary>
    /// Set by the notifier so the dialog can close itself when the user dismisses it.
    /// </summary>
    public Action? RequestClose { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Current { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    public partial int Total { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyPropertyChangedFor(nameof(CloseButtonText))]
    public partial bool IsComplete { get; set; }

    public bool IsRunning => !IsComplete;
    public bool IsIndeterminate => !IsComplete && Total == 0;
    public string CountText => $"{Current} of {Total} downloaded";
    public string CloseButtonText => IsComplete ? "Close" : "Cancel";

    public ImageDownloadWindowVM()
    {
        _cts = new CancellationTokenSource();
        _log = new StringBuilder();
        LogText = string.Empty;
    }

    /// <summary>
    /// Appends the outcome of a single image to the log and advances the counter. Called on the
    /// UI thread via <see cref="System.Progress{T}"/>.
    /// </summary>
    public void Report(ImageSyncProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Total = progress.Total;
        Current = progress.Current;
        AppendLine($"{(progress.Success ? "✓" : "✗")} {progress.RelativePath}");
    }

    /// <summary>
    /// Marks the run finished and appends a summary line.
    /// </summary>
    public void Finish(ImageSyncSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        if (summary.Total == 0)
            AppendLine("No missing images — everything is already present.");
        else if (summary.Failed == 0)
            AppendLine($"Done. Downloaded {summary.Downloaded} of {summary.Total}.");
        else
            AppendLine(
                $"Done. Downloaded {summary.Downloaded} of {summary.Total}, {summary.Failed} failed."
            );
        IsComplete = true;
    }

    /// <summary>
    /// Marks the run cancelled and appends a note.
    /// </summary>
    public void Cancelled()
    {
        AppendLine("Cancelled.");
        IsComplete = true;
    }

    private void AppendLine(string line)
    {
        _log.AppendLine(line);
        LogText = _log.ToString();
    }

    [RelayCommand]
    private void CloseOrCancel()
    {
        if (IsComplete)
            RequestClose?.Invoke();
        else
            _cts.Cancel();
    }

    public void Dispose() => _cts.Dispose();
}
