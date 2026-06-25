using AniTrack.Core.Interfaces;
using AniTrack.Infrastructure;
using AniTrack.Infrastructure.Data;
using AniTrack.ViewModels;
using AniTrack.ViewModels.Detail;
using AniTrack.ViewModels.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AniTrack;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniTrack");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(Path.Combine(AppDataPath, "covers"));
        Directory.CreateDirectory(Path.Combine(AppDataPath, "logs"));

        ConfigureSerilog();
        Services = BuildServiceProvider();

        await RunMigrationsAsync();

        var theme = await Services.GetRequiredService<ISettingsService>().GetThemeAsync();
        ApplyTheme(theme);

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog();
        });

        services.AddInfrastructure(AppDataPath);

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();

        // Pages
        services.AddTransient<Views.Library.AnimeLibraryPage>();
        services.AddTransient<Views.Library.MangaLibraryPage>();
        services.AddTransient<Views.Discover.DiscoverPage>();
        services.AddTransient<Views.Stats.StatsPage>();
        services.AddTransient<Views.Settings.SettingsPage>();
        services.AddTransient<Views.Detail.DetailPage>();

        // ViewModels
        services.AddTransient<ViewModels.Settings.SettingsViewModel>();
        services.AddTransient<AnimeLibraryViewModel>();
        services.AddTransient<MangaLibraryViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<ViewModels.Stats.StatsViewModel>();
        services.AddTransient<ViewModels.Discover.DiscoverViewModel>();

        return services.BuildServiceProvider();
    }

    private static async Task RunMigrationsAsync()
    {
        var catalogFactory = Services.GetRequiredService<IDbContextFactory<CatalogDbContext>>();
        await using var catalog = await catalogFactory.CreateDbContextAsync();
        await catalog.Database.MigrateAsync();

        var libraryFactory = Services.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
        await using var library = await libraryFactory.CreateDbContextAsync();
        await library.Database.MigrateAsync();
    }

    private static void ConfigureSerilog()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppDataPath, "logs", "anitrack_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    private static void ApplyTheme(string theme)
    {
        var appTheme = theme == "light" ? ApplicationTheme.Light : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(appTheme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
