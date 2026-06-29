using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RomForge.Core.IO;
using RomForge.Core.Services;
using RomForge.UI.Services;
using RomForge.UI.ViewModels;
using RomForge.UI.Views;
using Serilog;

namespace RomForge.UI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ConfigureLogging();

            MainWindow? mainWindow = null;
            IServiceProvider services = ConfigureServices(() => mainWindow);

            MainWindowVM vm = services.GetRequiredService<MainWindowVM>();
            DataContext = vm;
            MainWindow window = new MainWindow { DataContext = vm };
            mainWindow = window;
            desktop.MainWindow = window;
            window.Opened += async (_, _) => await vm.LoadManagedDatsAsync();
            desktop.Exit += (_, _) => Log.CloseAndFlush();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureLogging()
    {
        string logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RomForge",
            "logs"
        );

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                Path.Combine(logDir, "romforge-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        Log.Information("RomForge starting");
    }

    private static IServiceProvider ConfigureServices(Func<Window?> getWindow)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ILogger>(Log.Logger);
        services.AddSingleton<IFileDialogService>(_ => new AvaloniaFileDialogService(getWindow));
        services.AddSingleton<IUserNotifier>(_ => new AvaloniaUserNotifier(getWindow));
        services.AddSingleton<IRomSource, FileSystemRomSource>();
        services.AddSingleton<IRomFileOperations, LocalRomFileOperations>();
        services.AddSingleton<IArchiveCompressor, SevenZipCliCompressor>();
        services.AddSingleton<IArchiveExtractor>(sp =>
            new SharpCompressExtractor(sp.GetRequiredService<AppDataService>().TempPath));

        services.AddSingleton<HttpClient>(_ =>
        {
            HttpClient http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("RomForge/0.1");
            return http;
        });
        services.AddSingleton<Func<string, IDatReader>>(_ => path => new LocalDatReader(path));
        services.AddSingleton<AppDataService>();
        services.AddSingleton<IDatImporter, LocalDatImporter>();
        services.AddSingleton<IDatUpdateChecker, HttpDatUpdateChecker>();
        services.AddSingleton<IDatDownloader, HttpDatDownloader>();
        services.AddSingleton<DatConfigService>();
        services.AddSingleton<ScanResultStore>();
        services.AddSingleton<ReArchiveStore>();
        services.AddSingleton<AppPreferencesService>();
        services.AddSingleton<MainWindowVM>();
        return services.BuildServiceProvider();
    }
}
