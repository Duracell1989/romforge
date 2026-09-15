using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RomForge.UI.ViewModels
{
    public sealed partial class BatchProgressWindowVm : VmBase
    {
        private readonly CancellationTokenSource? _cts;

        public bool IsCancellable { get; }
        public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;
        public IReadOnlyList<BatchSlotVm> Slots { get; }
        public int Total { get; }
        public string CountText => $"{Completed} of {Total}";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CountText))]
        public partial int Completed { get; set; }

        public BatchProgressWindowVm(int total, int slotCount, bool isCancellable)
        {
            Total = total;
            IsCancellable = isCancellable;
            _cts = isCancellable ? new CancellationTokenSource() : null;
            Slots = Enumerable.Range(0, slotCount).Select(_ => new BatchSlotVm()).ToList();
        }

        [RelayCommand]
        private void Cancel() => _cts?.Cancel();
    }
}
