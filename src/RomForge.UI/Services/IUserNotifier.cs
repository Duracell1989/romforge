using System.Threading.Tasks;
using RomForge.UI.ViewModels;

namespace RomForge.UI.Services;

public interface IUserNotifier
{
    Task NotifyInfoAsync(string message);
    Task NotifyErrorAsync(string message);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowProgressAsync(string title, ProgressWindowVM vm, Task operationTask);
    Task ShowBatchProgressAsync(string title, BatchProgressWindowVM vm, Task operationTask);
    Task ShowImageDownloadAsync(ImageDownloadWindowVM vm, Task operationTask);
    Task ShowSettingsAsync(SettingsVM vm);
}
