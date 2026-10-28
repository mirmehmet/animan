using AniMan.Infrastructure.Data;
using AniMan.Infrastructure.Services;
using AniMan.Tests.Integration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AniMan.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly SqliteContextFactory<LibraryDbContext> _factory;
    private readonly SettingsService _service;

    public SettingsServiceTests()
    {
        _factory = new SqliteContextFactory<LibraryDbContext>(opts => new LibraryDbContext(opts));
        _service = new SettingsService(_factory, NullLogger<SettingsService>.Instance);
    }

    [Fact]
    public async Task GetAsync_WhenKeyDoesNotExist_ReturnsDefaultValue()
    {
        var theme = await _service.GetThemeAsync();
        theme.Should().Be("system");

        var lang = await _service.GetLanguageAsync();
        lang.Should().Be("en");

        var startup = await _service.GetStartupPageAsync();
        startup.Should().Be("Dashboard");

        var font = await _service.GetFontAsync();
        font.Should().Be("Segoe UI");

        var cacheDays = await _service.GetCacheRefreshDaysAsync();
        cacheDays.Should().Be(7);
    }

    [Fact]
    public async Task SetAsync_PersistsValue_AndRoundtripsSuccessfully()
    {
        await _service.SetThemeAsync("dark");
        (await _service.GetThemeAsync()).Should().Be("dark");

        await _service.SetLanguageAsync("tr");
        (await _service.GetLanguageAsync()).Should().Be("tr");

        await _service.SetStartupPageAsync("Anime");
        (await _service.GetStartupPageAsync()).Should().Be("Anime");

        await _service.SetFontAsync("Cascadia Code");
        (await _service.GetFontAsync()).Should().Be("Cascadia Code");
    }

    [Theory]
    [InlineData("14", 14)]
    [InlineData("1", 1)]
    [InlineData("365", 365)]
    [InlineData("-10", 1)]     // Clamped to minimum 1
    [InlineData("0", 1)]       // Clamped to minimum 1
    [InlineData("1000", 365)]  // Clamped to maximum 365
    [InlineData("invalid", 7)] // Defaults to 7
    [InlineData("", 7)]        // Defaults to 7
    public async Task GetCacheRefreshDaysAsync_ClampsBetween1And365(string input, int expected)
    {
        _service.InvalidateCache();
        await _service.SetAsync("CacheRefreshDays", input);

        var days = await _service.GetCacheRefreshDaysAsync();
        days.Should().Be(expected);
    }

    [Fact]
    public async Task ConcurrentSetAsync_SucceedsWithoutDeadlock()
    {
        var tasks = Enumerable.Range(1, 10).Select(i =>
            _service.SetAsync($"Key_{i}", $"Value_{i}"));

        await Task.WhenAll(tasks);

        for (int i = 1; i <= 10; i++)
        {
            var val = await _service.GetAsync($"Key_{i}");
            val.Should().Be($"Value_{i}");
        }
    }

    public void Dispose()
    {
        _service.Dispose();
        _factory.Dispose();
    }
}

