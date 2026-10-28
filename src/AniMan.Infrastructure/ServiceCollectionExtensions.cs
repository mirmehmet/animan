using System.Net;
using System.Net.Sockets;
using AniMan.Core.Common;
using AniMan.Core.Interfaces;
using AniMan.Infrastructure.AniList;
using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.MediaSource;
using AniMan.Infrastructure.Services;
using AniMan.Infrastructure.Tenrai;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AniMan.Infrastructure;

public static class ServiceCollectionExtensions
{
    private const long MaxCoverResponseBytes = 10L * 1024 * 1024; // 10 MB

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string appDataPath)
    {
        var catalogPath = Path.Combine(appDataPath, "catalog.db");
        var libraryPath = Path.Combine(appDataPath, "library.db");
        var coversPath = Path.Combine(appDataPath, "covers");

        Directory.CreateDirectory(coversPath);

        var catalogConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = catalogPath,
            DefaultTimeout = 30,
            ForeignKeys = true
        }.ToString();

        var libraryConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = libraryPath,
            DefaultTimeout = 30,
            ForeignKeys = true
        }.ToString();

        var pragmaInterceptor = new SqlitePragmaInterceptor();

        services.AddDbContextFactory<CatalogDbContext>(options =>
            options.UseSqlite(catalogConnectionString).AddInterceptors(pragmaInterceptor));

        services.AddDbContextFactory<LibraryDbContext>(options =>
            options.UseSqlite(libraryConnectionString).AddInterceptors(pragmaInterceptor));

        services.AddSingleton(new StoragePaths(appDataPath, coversPath));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<TenraiRateLimiter>();

        services.AddHttpClient<TenraiClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.tenrai.org/v1/");
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
        services.AddTransient<IMediaSourceClient>(sp => new FallbackMediaClient(
            sp.GetRequiredService<TenraiClient>(),
            sp.GetRequiredService<AniListClient>(),
            sp.GetRequiredService<ILogger<FallbackMediaClient>>()));

        services.AddHttpClient("covers", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.MaxResponseContentBufferSize = MaxCoverResponseBytes;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AniMan/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            MaxAutomaticRedirections = 5,
            ConnectCallback = ConnectSafeExternalAsync
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

    private static async ValueTask<Stream> ConnectSafeExternalAsync(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        if (HttpUrlGuard.IsLocalHostname(host))
            throw new HttpRequestException($"Blocked cover connection to local host '{host}'.");

        var addresses = IPAddress.TryParse(host, out var literalIp)
            ? [literalIp]
            : await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);

        if (addresses.Length == 0 || addresses.Any(HttpUrlGuard.IsPrivateOrLoopbackIp))
            throw new HttpRequestException($"Blocked cover connection to non-public address for host '{host}'.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
