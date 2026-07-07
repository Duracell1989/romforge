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
        Window? window = _getWindow();
        if (window is null)
            return;

        await MessageBoxManager
            .GetMessageBoxStandard("Information", message, ButtonEnum.Ok, Icon.Info)
            .ShowWindowDialogAsync(window);
    }

    public async Task NotifyErrorAsync(string message)
    {
        Window? window = _getWindow();
        if (window is null)
            return;

        await MessageBoxManager
            .GetMessageBoxStandard("Error", message, ButtonEnum.Ok, Icon.Error)
            .ShowWindowDialogAsync(window);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        Window? window = _getWindow();
        if (window is null)
            return false;

        ButtonResult result = await MessageBoxManager
            .GetMessageBoxStandard(title, message, ButtonEnum.YesNo, Icon.Info)
            .ShowWindowDialogAsync(window);

        return result == ButtonResult.Yes;
    }

    public async Task ShowProgressAsync(string title, ProgressWindowVM vm, Task operationTask)
    {
        Window? parent = _getWindow();
        ProgressWindow window = new ProgressWindow { Title = title, DataContext = vm };

        if (parent is not null)
        {
            bool operationCompleted = false;
            bool userInitiatedClose = false;

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
            bool operationCompleted = false;
            bool userInitiatedClose = false;

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
