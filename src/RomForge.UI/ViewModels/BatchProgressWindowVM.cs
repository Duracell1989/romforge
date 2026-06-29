using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RomForge.UI.ViewModels;

public sealed partial class BatchProgressWindowVM : VMBase
{
    private readonly CancellationTokenSource? _cts;

    public bool IsCancellable { get; }
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;
    public IReadOnlyList<BatchSlotVM> Slots { get; }
    public int Total { get; }
    public string CountText => $"{Completed} of {Total}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Completed { get; set; }

    public BatchProgressWindowVM(int total, int slotCount, bool isCancellable)
    {
        Total = total;
        IsCancellable = isCancellable;
        _cts = isCancellable ? new CancellationTokenSource() : null;
        Slots = Enumerable.Range(0, slotCount).Select(_ => new BatchSlotVM()).ToList();
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
