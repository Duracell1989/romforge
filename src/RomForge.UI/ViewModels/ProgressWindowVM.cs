using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RomForge.UI.ViewModels;

public sealed partial class ProgressWindowVM : VMBase
{
    private readonly CancellationTokenSource? _cts;

    public bool IsCancellable { get; }
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Current { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    public partial int Total { get; set; }

    public bool IsIndeterminate => Total == 0;

    [ObservableProperty]
    public partial int Progress { get; set; }

    [ObservableProperty]
    public partial string? CurrentFile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPhase))]
    public partial string Phase { get; set; }

    public bool HasPhase => !string.IsNullOrEmpty(Phase);

    public string CountText => $"{Current} of {Total}";

    public ProgressWindowVM() : this(100, false)
    {
    }

    public ProgressWindowVM(int total, bool isCancellable)
    {
        _cts = isCancellable ? new CancellationTokenSource() : null;
        Total = total;
        IsCancellable = isCancellable;
        Phase = string.Empty;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
