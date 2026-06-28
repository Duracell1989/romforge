using CommunityToolkit.Mvvm.ComponentModel;

namespace RomForge.UI.ViewModels;

public sealed partial class BatchSlotVM : VMBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    public partial string? FileName { get; set; }

    [ObservableProperty]
    public partial int Progress { get; set; }

    public bool IsActive => FileName is not null;
}
