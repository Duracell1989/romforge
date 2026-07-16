using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using RomForge.UI.ViewModels;
using RomForge.UI.Views;

namespace RomForge.UI.Services;

internal sealed class AvaloniaUserNotifier : IUserNotifier
{
    private readonly Func<Window?> _getWindow;

    public AvaloniaUserNotifier(Func<Window?> getWindow)
    {
        _getWindow = getWindow;
    }

    public async Task NotifyInfoAsync(string message)
    {
        var window = _getWindow();
        if (window is null)
            return;

        await MessageBoxManager
            .GetMessageBoxStandard("Information", message, ButtonEnum.Ok, Icon.Info)
            .ShowWindowDialogAsync(window);
    }

    public async Task NotifyErrorAsync(string message)
    {
        var window = _getWindow();
        if (window is null)
            return;

        await MessageBoxManager
            .GetMessageBoxStandard("Error", message, ButtonEnum.Ok, Icon.Error)
            .ShowWindowDialogAsync(window);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var window = _getWindow();
        if (window is null)
            return false;

        var result = await MessageBoxManager
            .GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Info)
            .ShowWindowDialogAsync(window);

        return result == ButtonResult.Yes;
    }

    public async Task ShowProgressAsync(string title, ProgressWindowVM vm, Task operationTask)
    {
        var parent = _getWindow();
        var window = new ProgressWindow { Title = title, DataContext = vm };

        if (parent is not null)
        {
            var operationCompleted = false;
            var userInitiatedClose = false;

            window.Closing += (_, e) =>
            {
                if (operationCompleted)
                    return;
                if (vm.IsCancellable)
                {
                    userInitiatedClose = true;
                    vm.CancelCommand.Execute(null);
                }
                else
                    e.Cancel = true;
            };

            window.Opened += (_, _) =>
            {
                _ = operationTask.ContinueWith(
                    _ =>
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            operationCompleted = true;
                            if (!userInitiatedClose)
                                window.Close();
                        }),
                    TaskScheduler.Default
                );
            };

            await window.ShowDialog(parent);
        }
        else
        {
            window.Show();
            await operationTask;
            window.Close();
        }
    }

    public async Task ShowImageDownloadAsync(ImageDownloadWindowVM vm, Task operationTask)
    {
        var parent = _getWindow();
        var window = new ImageDownloadWindow { DataContext = vm };
        vm.RequestClose = () => window.Close();

        // While the download is running, the OS close button cancels but keeps the window open
        // until the operation observes the cancellation; the log stays readable afterwards.
        window.Closing += (_, e) =>
        {
            if (!vm.IsComplete)
            {
                vm.CloseOrCancelCommand.Execute(null);
                e.Cancel = true;
            }
        };

        if (parent is not null)
            await window.ShowDialog(parent);
        else
            window.Show();

        await operationTask;
    }

    public async Task ShowSettingsAsync(SettingsVM vm)
    {
        Window? parent = _getWindow();
        SettingsWindow window = new SettingsWindow { DataContext = vm };
        vm.RequestClose = _ => window.Close();

        if (parent is not null)
            await window.ShowDialog(parent);
        else
            window.Show();
    }

    public async Task ShowBatchProgressAsync(
        string title,
        BatchProgressWindowVM vm,
        Task operationTask
    )
    {
        Window? parent = _getWindow();
        BatchProgressWindow window = new BatchProgressWindow { Title = title, DataContext = vm };

        if (parent is not null)
        {
            var operationCompleted = false;
            var userInitiatedClose = false;

            window.Closing += (_, e) =>
            {
                if (operationCompleted)
                    return;
                if (vm.IsCancellable)
                {
                    userInitiatedClose = true;
                    vm.CancelCommand.Execute(null);
                }
                else
                    e.Cancel = true;
            };

            window.Opened += (_, _) =>
            {
                _ = operationTask.ContinueWith(
                    _ =>
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            operationCompleted = true;
                            if (!userInitiatedClose)
                                window.Close();
                        }),
                    TaskScheduler.Default
                );
            };

            await window.ShowDialog(parent);
        }
        else
        {
            window.Show();
            await operationTask;
            window.Close();
        }
    }
}
