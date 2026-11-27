using System.Globalization;
using AniMan.Core.Common;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class PartialDateTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    [Theory]
    [InlineData(1989, null, null, "1989")]
    [InlineData(2024, 4, null, "2024-04")]
    [InlineData(2016, 1, 14, "2016-01-14T00:00:00+00:00")]
    [InlineData(null, 4, 1, null)]
    [InlineData(2024, 13, null, null)]
    [InlineData(2023, 2, 30, null)]
    public void ToStored_KeepsOnlyTheKnownParts(int? year, int? month, int? day, string? expected) =>
        PartialDate.ToStored(year, month, day).Should().Be(expected);

    [Fact]
    public void MonthYear_YearOnly_ShowsJustTheYear()
    {
        PartialDate.MonthYear("1989", English).Should().Be("1989");
        PartialDate.MonthYear("1989", Turkish).Should().Be("1989");
    }

    [Fact]
    public void MonthYear_YearAndMonth_ShowsMonthInTheUiLanguage()
    {
        PartialDate.MonthYear("2024-04", English).Should().Be("Apr 2024");
        PartialDate.MonthYear("2024-04", Turkish).Should().Be("Nis 2024");
    }

    [Fact]
    public void MonthYear_FullDate_ReadAsWrittenNotShiftedToLocalTime()
    {
        // Midnight UTC on 1 Jan: in a time zone behind UTC this used to show as December.
        PartialDate.MonthYear("2016-01-01T00:00:00+00:00", English).Should().Be("Jan 2016");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("not a date")]
    [InlineData("2024-13")]
    public void MonthYear_BlankOrUnreadable_IsNull(string? stored) =>
        PartialDate.MonthYear(stored, English).Should().BeNull();
}
