using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Localization;
using AniMan.Navigation;
using AniMan.ViewModels;
using AniMan.ViewModels.Dashboard;
using AniMan.ViewModels.Detail;
using AniMan.ViewModels.Library;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace AniMan;

public partial class App : Application
{
    private static bool _languageMetadataOverridden;
    private static bool _restartRequested;

    public static IServiceProvider Services { get; private set; } = null!;

    private static Serilog.ILogger AppLogger => Log.ForContext<App>();

    private static string AppDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AniMan");

    public static void RequestRestart()
    {
        _restartRequested = true;
        Current?.Shutdown();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(Path.Combine(AppDataPath, "covers"));
        Directory.CreateDirectory(Path.Combine(AppDataPath, "logs"));

        ConfigureSerilog();

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

            // Purge soft-deleted items past TrashPolicy.RetentionDays on every startup
            var tracking = Services.GetRequiredService<ITrackingService>();
            await tracking.PurgeExpiredTrashAsync();
            Services.GetRequiredService<CoverStore>().DeleteLeftoverTempFiles();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
            Theming.AppThemeManager.SyncWindowBackground(mainWindow);

            // SystemThemeWatcher needs a window handle — attach AFTER Show(), without
            // re-applying the theme (which would briefly reset DynamicResource values).
            if (themeMode == ViewModels.Settings.ThemeMode.System)
                Theming.AppThemeManager.AttachSystemWatcher(mainWindow);

            // Fill extended metadata for snapshots from older versions/backups, in the
            // background; stopped on exit like every other background job (AM-074).
            var backfill = Services.GetRequiredService<SnapshotBackfillService>();
            Services.GetRequiredService<BackgroundWork>().Run("snapshot backfill", backfill.RunAsync);
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

    /// <summary>1 while the "unexpected error" box is open.</summary>
    private static int _showingErrorBox;

    // Registered here rather than in OnStartup, so failures while setting up folders and the
    // log are caught too (AM-023). Until Serilog is configured, those go to its silent default.
    public App() => RegisterGlobalExceptionHandlers();

    private void RegisterGlobalExceptionHandlers()
    {
        // Last-resort net: individual operations report failures via Result;
        // anything reaching these handlers is a bug, not an expected error path.
        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Fatal(args.Exception, "Unhandled dispatcher exception");

            // Carrying on after these would run on corrupted state: let the app end
            // (the AppDomain handler below flushes the log).
            if (IsUnrecoverable(args.Exception))
                return;

            args.Handled = true;

            // The box runs its own message loop. A failure that repeats on every layout or
            // render pass would otherwise stack one box on top of another; while one is
            // open, further failures are only logged (AM-023).
            if (Interlocked.Exchange(ref _showingErrorBox, 1) == 1)
                return;
            try
            {
                System.Windows.MessageBox.Show(
                    LocalizationManager.Get("App_UnexpectedError"),
                    LocalizationManager.Get("App_UnexpectedErrorTitle"),
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                Volatile.Write(ref _showingErrorBox, 0);
            }
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

    /// <summary>Failures after which the process state cannot be trusted.</summary>
    private static bool IsUnrecoverable(Exception ex) =>
        ex is OutOfMemoryException or InsufficientExecutionStackException or AccessViolationException
            or InvalidProgramException or BadImageFormatException;

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
        services.AddSingleton<Wpf.Ui.Abstractions.INavigationViewPageProvider>(
            sp => new ServiceProviderPageProvider(sp));
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IContentDialogService, ContentDialogService>();
        services.AddSingleton<ISnackbarService, SnackbarService>();
        services.AddSingleton<NavigationBag>();
        services.AddSingleton<DetailNavigator>();
        services.AddSingleton<BusyOverlayViewModel>();

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
        services.AddSingleton<LibraryLayoutViewModel>(); // cover size, shared by both library pages
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
        try
        {
            var backup = await LibraryMigrationBackup.BackupIfMigrationsPendingAsync(
                library, Path.Combine(AppDataPath, "backups"));
            if (backup is not null)
                AppLogger.Information("Library backed up before update: {Path}", backup);
        }
        catch (Exception ex)
        {
            // A failed copy must not lock the user out of the app; the migrations themselves
            // are covered by tests that run them against populated older databases.
            AppLogger.Error(ex, "Could not back up the library before the update; migrating anyway");
        }
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
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private static void ApplyTheme(ViewModels.Settings.ThemeMode mode) =>
        Theming.AppThemeManager.Apply(mode);

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Information("AniMan shutting down (restart: {Restart})", _restartRequested);
        try
        {
            // Stop background work first and give it a moment: disposing the services under
            // a running cache refresh or backfill left it working against disposed databases.
            if (Services?.GetService<BackgroundWork>() is { } background &&
                !background.StopAndWait(TimeSpan.FromSeconds(2)))
            {
                AppLogger.Warning("Background work was still running at shutdown");
            }
            (Services as IDisposable)?.Dispose();
            SqliteConnection.ClearAllPools();
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Error while disposing services during shutdown");
        }

        Log.CloseAndFlush();
        base.OnExit(e);

        if (_restartRequested && Environment.ProcessPath is { Length: > 0 } exePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Logger is already closed; avoid throwing during final process exit.
            }
        }
    }

    private sealed class ServiceProviderPageProvider : Wpf.Ui.Abstractions.INavigationViewPageProvider
    {
        private readonly IServiceProvider _sp;
        public ServiceProviderPageProvider(IServiceProvider sp) => _sp = sp;
        public object? GetPage(Type pageType) => _sp.GetService(pageType);
    }
}
