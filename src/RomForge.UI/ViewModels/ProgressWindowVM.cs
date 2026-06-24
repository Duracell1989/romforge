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
    public partial int Total { get; set; }

    [ObservableProperty]
    public partial int Progress { get; set; }

    [ObservableProperty]
    public partial string? CurrentFile { get; set; }

    public string CountText => $"{Current} of {Total}";

    public ProgressWindowVM(int total, bool isCancellable)
    {
        _cts = isCancellable ? new CancellationTokenSource() : null;
        Total = total;
        IsCancellable = isCancellable;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
