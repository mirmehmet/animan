using System.Net;
using AniMan.Infrastructure.AniList;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.AniList;

public class AniListClientTests : IDisposable
{
    private readonly AniListRateLimiter _limiter = new(NullLogger<AniListRateLimiter>.Instance);

    private sealed class DelegatingMockHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handlerFunc(request, cancellationToken);
    }

    [Fact]
    public async Task SearchAnime_WhenSourceReturnsValidGraphQL_MapsToDto()
    {
        var json = """
            {
                "data": {
                    "Page": {
                        "pageInfo": { "hasNextPage": false },
                        "media": [
                            {
                                "idMal": 1,
                                "title": { "romaji": "Cowboy Bebop", "english": "Cowboy Bebop", "native": "カウボーイビバップ" },
                                "description": "Bebop synopsis",
                                "format": "TV",
                                "status": "FINISHED",
                                "episodes": 26,
                                "averageScore": 88,
                                "genres": ["Action", "Sci-Fi"]
                            }
                        ]
                    }
                }
            }
            """;

        var handler = new DelegatingMockHandler((req, ct) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co") };
        var client = new AniListClient(http, _limiter, NullLogger<AniListClient>.Instance);

        var result = await client.SearchAnimeAsync("cowboy");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Data.Should().ContainSingle();
        result.Value!.Data![0].MalId.Should().Be(1);
        result.Value!.Data![0].Title.Should().Be("Cowboy Bebop");
    }

    [Fact]
    public async Task SearchAnime_WhenUserCancels_ThrowsOperationCanceledExceptionDirectly()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var handler = new DelegatingMockHandler((req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co") };
        var client = new AniListClient(http, _limiter, NullLogger<AniListClient>.Instance);

        var act = async () => await client.SearchAnimeAsync("cowboy", ct: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SearchAnime_WhenNetworkTimesOut_ReturnsFailureWithoutCrashing()
    {
        var handler = new DelegatingMockHandler((req, ct) =>
            throw new TaskCanceledException("HttpClient timeout"));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co") };
        var client = new AniListClient(http, _limiter, NullLogger<AniListClient>.Instance);

        var result = await client.SearchAnimeAsync("cowboy");

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("timed out");
    }

    [Fact]
    public async Task GetAnimeFull_WhenSourceReturns404_ReturnsFailureWithoutCrashing()
    {
        var handler = new DelegatingMockHandler((req, ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graphql.anilist.co") };
        var client = new AniListClient(http, _limiter, NullLogger<AniListClient>.Instance);

        var result = await client.GetAnimeFullAsync(999999);

        result.IsSuccess.Should().BeFalse();
    }

    public void Dispose() => _limiter.Dispose();
}

