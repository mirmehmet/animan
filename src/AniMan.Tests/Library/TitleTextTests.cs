using System.Globalization;
using AniMan.Core.Library;
using FluentAssertions;

namespace AniMan.Tests.Library;

/// <summary>
/// Search must give the same answer whatever culture the machine or the app runs in, so
/// every case runs under both tr-TR (a Turkish developer PC) and en-US (CI).
/// </summary>
public class TitleTextTests
{
    [Theory]
    [InlineData("Inuyasha", "inu")]
    [InlineData("Inuyasha", "İnu")]
    [InlineData("Inuyasha", "ınu")]
    [InlineData("Inuyasha", "INU")]
    [InlineData("İstanbul", "istanbul")]
    [InlineData("İstanbul", "Istanbul")]
    [InlineData("Pokémon", "pokemon")]
    [InlineData("Pokemon", "Pokémon")]
    [InlineData("Shingeki no Kyōjin", "kyojin")]
    [InlineData("Çılgın", "cilgin")]
    [InlineData("Kimi no Na wa.", "  na wa ")]
    [InlineData("Anything", "")]
    [InlineData("Anything", null)]
    public void Matches_IgnoresCaseAccentsAndTurkishI(string title, string? query) =>
        InBothCultures(() => TitleText.Matches(TitleText.SearchKey(title), query).Should().BeTrue());

    [Theory]
    [InlineData("Inuyasha", "inuyashaa")]
    [InlineData("Kimi no Na wa.", "kimino")]
    [InlineData("Dororo", "doro ro")]
    public void Matches_RejectsTextThatIsNotThere(string title, string query) =>
        InBothCultures(() => TitleText.Matches(TitleText.SearchKey(title), query).Should().BeFalse());

    [Fact]
    public void SearchKey_IsTheSameInEveryCulture()
    {
        var keys = new List<string>();
        InBothCultures(() => keys.Add(TitleText.SearchKey("İNUYASHA ılık Çağ Pokémon")));

        keys.Should().AllBe("inuyasha ilik cag pokemon");
    }

    private static void InBothCultures(Action assert)
    {
        var original = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            foreach (var name in new[] { "tr-TR", "en-US" })
            {
                var culture = CultureInfo.GetCultureInfo(name);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                assert();
            }
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = original;
        }
    }
}
