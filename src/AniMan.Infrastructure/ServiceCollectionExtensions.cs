using AniMan.Core.Interfaces;
using AniMan.Infrastructure.AniList;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string appDataPath)
    {
        var catalogPath = Path.Combine(appDataPath, "catalog.db");
        var libraryPath = Path.Combine(appDataPath, "library.db");
        var coversPath = Path.Combine(appDataPath, "covers");

        Directory.CreateDirectory(coversPath);

        services.AddDbContextFactory<CatalogDbContext>(options =>
            options.UseSqlite($"Data Source={catalogPath}"));

        services.AddDbContextFactory<LibraryDbContext>(options =>
            options.UseSqlite($"Data Source={libraryPath}"));

        services.AddSingleton(new StoragePaths(appDataPath, coversPath));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<JikanRateLimiter>();

        services.AddHttpClient<JikanClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.jikan.moe/v4/");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddSingleton<AniListRateLimiter>();
        services.AddHttpClient<AniListClient>(client =>
        {
            client.BaseAddress = new Uri("https://graphql.anilist.co");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Only the fallback pair is injectable: the concrete clients are registered above
        // as themselves, so nothing can accidentally bind straight to a single source.
        services.AddTransient<IJikanClient>(sp => new FallbackMediaClient(
            sp.GetRequiredService<JikanClient>(),
            sp.GetRequiredService<AniListClient>(),
            sp.GetRequiredService<ILogger<FallbackMediaClient>>()));

        services.AddHttpClient("covers", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AniMan/1.0");
        });

        services.AddSingleton<CoverStore>();
        services.AddTransient<ICatalogService, CatalogService>();
        services.AddTransient<ISnapshotService, SnapshotService>();
        services.AddTransient<ITrackingService, TrackingService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddTransient<IExportService, ExportService>();
        services.AddTransient<IDataManagementService, DataManagementService>();

        return services;
    }
}
