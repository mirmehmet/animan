using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Navigation;
using AniMan.Infrastructure.Data;
using AniMan.Localization;
using AniMan.ViewModels;
using AniMan.ViewModels.Dashboard;
using AniMan.ViewModels.Detail;
using AniMan.ViewModels.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniMan");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(Path.Combine(AppDataPath, "covers"));
        Directory.CreateDirectory(Path.Combine(AppDataPath, "logs"));

        ConfigureSerilog();
        RegisterGlobalExceptionHandlers();

        try
        {
            Services = BuildServiceProvider();

            await RunMigrationsAsync();

            var settings = Services.GetRequiredService<ISettingsService>();

            var theme = await settings.GetThemeAsync();
            var themeMode = Theming.AppThemeManager.Parse(theme);
            ApplyTheme(themeMode);

            var font = await settings.GetFontAsync();
            Theming.AppThemeManager.ApplyFont(font);

            var language = await settings.GetLanguageAsync();
            LocalizationManager.SetCulture(language);

            // Purge soft-deleted items older than 30 days on every startup
            var tracking = Services.GetRequiredService<ITrackingService>();
            await tracking.PurgeExpiredTrashAsync();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // SystemThemeWatcher needs a window handle — attach AFTER Show(), without
            // re-applying the theme (which would briefly reset DynamicResource values).
            if (themeMode == ViewModels.Settings.ThemeMode.System)
                Theming.AppThemeManager.AttachSystemWatcher(mainWindow);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed");
            System.Windows.MessageBox.Show(
                LocalizationManager.Get("App_StartupFailed"),
                "AniMan", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // Last-resort net: individual operations report failures via Result;
        // anything reaching these handlers is a bug, not an expected error path.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fatal(args.Exception, "Unhandled dispatcher exception");
            System.Windows.MessageBox.Show(
                LocalizationManager.Get("App_UnexpectedError"),
                LocalizationManager.Get("App_UnexpectedErrorTitle"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception,
                "Unhandled AppDomain exception (terminating: {IsTerminating})", args.IsTerminating);
            Log.CloseAndFlush();
        };
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
        services.AddSingleton<Wpf.Ui.Abstractions.INavigationViewPageProvider>(
            sp => new ServiceProviderPageProvider(sp));
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<NavigationBag>();

        // Pages
        services.AddTransient<Views.Dashboard.DashboardPage>();
        services.AddTransient<Views.Library.AnimeLibraryPage>();
        services.AddTransient<Views.Library.MangaLibraryPage>();
        services.AddTransient<Views.Discover.DiscoverPage>();
        services.AddTransient<Views.Stats.StatsPage>();
        services.AddTransient<Views.Settings.SettingsPage>();
        services.AddTransient<Views.Detail.DetailPage>();

        // ViewModels
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ViewModels.Settings.SettingsViewModel>();
        services.AddTransient<AnimeLibraryViewModel>();
        services.AddTransient<MangaLibraryViewModel>();
        services.AddTransient<DetailViewModel>();
        services.AddTransient<ViewModels.Stats.StatsViewModel>();
        // Singleton: keeps tab/search/results/scroll alive so navigating to a
        // detail page and back (or to any other page) doesn't reset Discover.
        services.AddSingleton<ViewModels.Discover.DiscoverViewModel>();

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
                Path.Combine(AppDataPath, "logs", "AniMan_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();
    }

    private static void ApplyTheme(ViewModels.Settings.ThemeMode mode) =>
        Theming.AppThemeManager.Apply(mode);

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private sealed class ServiceProviderPageProvider : Wpf.Ui.Abstractions.INavigationViewPageProvider
    {
        private readonly IServiceProvider _sp;
        public ServiceProviderPageProvider(IServiceProvider sp) => _sp = sp;
        public object? GetPage(Type pageType) => _sp.GetService(pageType);
    }
}
