using AniMan.Core.Common;
using FluentAssertions;

namespace AniMan.Tests.Common;

public class NameListTests
{
    [Theory]
    [InlineData("""["Action","Drama"]""", new[] { "Action", "Drama" })]
    [InlineData("[]", new string[0])]
    [InlineData(null, new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("not json", new string[0])]
    [InlineData("""{"a":1}""", new string[0])]
    public void Parse_ReadsAJsonArray_AndNeverThrows(string? json, string[] expected)
    {
        NameList.Parse(json).Should().Equal(expected);
    }

    [Fact]
    public void ToJson_RoundTripsThroughParse()
    {
        NameList.Parse(NameList.ToJson(["Slice of Life", "Ação"])).Should().Equal("Slice of Life", "Ação");
    }

    [Theory]
    [InlineData("""["Action","Drama"]""", """["Action","Drama"]""")] // clean: kept as written
    [InlineData("""["Action",null,"  ","Drama"]""", """["Action","Drama"]""")]
    [InlineData("""[1,2]""", null)]
    [InlineData("oops", null)]
    [InlineData("", null)]
    public void Clean_KeepsOnlyRealNameArrays(string json, string? expected)
    {
        NameList.Clean(json).Should().Be(expected);
    }
}

public class TrashPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 29)]
    [InlineData(29, 1)]
    [InlineData(30, 0)]
    [InlineData(45, 0)]
    public void DaysLeft_CountsDownFromTheRetentionPeriod(int daysAgo, int expected)
    {
        TrashPolicy.DaysLeft(Now.AddDays(-daysAgo), Now).Should().Be(expected);
    }
}
