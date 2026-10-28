using System.Globalization;
using AniMan.Core.Common;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class RatingParserTests
{
    [Theory]
    [InlineData("8,5", 8.5)]
    [InlineData("8.5", 8.5)]
    [InlineData(" 9,25 ", 9.3)]
    [InlineData("10", 10.0)]
    [InlineData("0", 0.0)]
    [InlineData("99", 10.0)]
    [InlineData("-4,5", 0.0)]
    public void TryParse_AcceptsDotAndCommaAcrossCultures(string input, double expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var cultureName in new[] { "tr-TR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                RatingParser.TryParse(input, out var rating).Should().BeTrue();
                rating.Should().Be((decimal)expected);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_EmptyOrWhitespace_ClearsRating(string? input)
    {
        RatingParser.TryParse(input, out var rating).Should().BeTrue();
        rating.Should().BeNull();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("8,5,2")]
    [InlineData("8.5.2")]
    public void TryParse_InvalidText_ReturnsFalse(string input)
    {
        RatingParser.TryParse(input, out var rating).Should().BeFalse();
        rating.Should().BeNull();
    }
}

