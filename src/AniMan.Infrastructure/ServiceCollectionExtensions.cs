using AniMan.Core.Interfaces;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Jikan;
using AniMan.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddTransient<IJikanClient>(sp => sp.GetRequiredService<JikanClient>());

        services.AddHttpClient("covers", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AniMan/1.0");
        });

        services.AddTransient<ICatalogService, CatalogService>();
        services.AddTransient<ISnapshotService, SnapshotService>();
        services.AddTransient<ITrackingService, TrackingService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddTransient<IExportService, ExportService>();
        services.AddTransient<IDataManagementService, DataManagementService>();

        return services;
    }
}
