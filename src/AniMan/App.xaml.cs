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
using System.Windows.Markup;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class App : Application
{
    private static bool _languageMetadataOverridden;

    public static IServiceProvider Services { get; private set; } = null!;

    private static Serilog.ILogger AppLogger => Log.ForContext<App>();

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

        AppLogger.Information("AniMan starting up");

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
            ApplyXmlLanguageMetadata();

            // Purge soft-deleted items older than 30 days on every startup
            var tracking = Services.GetRequiredService<ITrackingService>();
            await tracking.PurgeExpiredTrashAsync();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            Theming.AppThemeManager.SyncWindowBackground(mainWindow);

            // SystemThemeWatcher needs a window handle — attach AFTER Show(), without
            // re-applying the theme (which would briefly reset DynamicResource values).
            if (themeMode == ViewModels.Settings.ThemeMode.System)
                Theming.AppThemeManager.AttachSystemWatcher(mainWindow);
        }
        catch (Exception ex)
        {
            AppLogger.Fatal(ex, "Startup failed");
            System.Windows.MessageBox.Show(
                LocalizationManager.Get("App_StartupFailed"),
                "AniMan", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ApplyXmlLanguageMetadata()
    {
        if (_languageMetadataOverridden)
            return;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage(LocalizationManager.Culture.IetfLanguageTag)));
        _languageMetadataOverridden = true;
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // Last-resort net: individual operations report failures via Result;
        // anything reaching these handlers is a bug, not an expected error path.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Fatal(args.Exception, "Unhandled dispatcher exception");
            System.Windows.MessageBox.Show(
                LocalizationManager.Get("App_UnexpectedError"),
                LocalizationManager.Get("App_UnexpectedErrorTitle"),
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            AppLogger.Fatal(args.ExceptionObject as Exception,
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
            logging.AddSerilog(dispose: true);
        });

        services.AddInfrastructure(AppDataPath);

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<Wpf.Ui.Abstractions.INavigationViewPageProvider>(
            sp => new ServiceProviderPageProvider(sp));
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<NavigationBag>();

        // Pages: Main tab pages are Singletons so their VisualTrees are preserved and not leaked/duplicated across navigations.
        // DetailPage is Transient as it represents individual items on demand.
        services.AddSingleton<Views.Dashboard.DashboardPage>();
        services.AddSingleton<Views.Library.AnimeLibraryPage>();
        services.AddSingleton<Views.Library.MangaLibraryPage>();
        services.AddSingleton<Views.Discover.DiscoverPage>();
        services.AddSingleton<Views.Stats.StatsPage>();
        services.AddSingleton<Views.Settings.SettingsPage>();
        services.AddTransient<Views.Detail.DetailPage>();

        // ViewModels
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ViewModels.Settings.SettingsViewModel>();
        services.AddSingleton<AnimeLibraryViewModel>();
        services.AddSingleton<MangaLibraryViewModel>();
        services.AddSingleton<ViewModels.Stats.StatsViewModel>();
        services.AddSingleton<ViewModels.Discover.DiscoverViewModel>();
        services.AddTransient<DetailViewModel>();

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
        const long maxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

        Log.Logger = new LoggerConfiguration()
#if DEBUG
            .MinimumLevel.Debug()
#else
            .MinimumLevel.Information()
#endif
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppDataPath, "logs", "AniMan_.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: maxFileSizeBytes,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ApplyTheme(ViewModels.Settings.ThemeMode mode) =>
        Theming.AppThemeManager.Apply(mode);

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Information("AniMan shutting down");
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
