using AniTrack.Core.Interfaces;
using AniTrack.Infrastructure.Data;
using AniTrack.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AniTrack.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string appDataPath)
    {
        var catalogPath = Path.Combine(appDataPath, "catalog.db");
        var libraryPath = Path.Combine(appDataPath, "library.db");

        services.AddDbContextFactory<CatalogDbContext>(options =>
            options.UseSqlite($"Data Source={catalogPath}"));

        services.AddDbContextFactory<LibraryDbContext>(options =>
            options.UseSqlite($"Data Source={libraryPath}"));

        services.AddSingleton<ISettingsService, SettingsService>();

        return services;
    }
}
