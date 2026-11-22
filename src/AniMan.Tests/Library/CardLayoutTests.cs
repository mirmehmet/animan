using AniMan.Core.Library;
using FluentAssertions;

namespace AniMan.Tests.Library;

public class CardLayoutTests
{
    [Fact]
    public void DefaultStep_IsTodaysCardWidth()
    {
        CardSizes.WidthOf(CardSizes.DefaultStep).Should().Be(150);
        CardSizes.CoverHeightOf(150).Should().Be(200);
    }

    [Theory]
    [InlineData(0, CardDetail.Compact)]
    [InlineData(1, CardDetail.Compact)]
    [InlineData(2, CardDetail.Standard)]
    [InlineData(3, CardDetail.Detailed)]
    [InlineData(4, CardDetail.Detailed)]
    public void DetailOf_MapsStepsToInfoLevels(int step, CardDetail expected) =>
        CardSizes.DetailOf(step).Should().Be(expected);

    [Theory]
    [InlineData(-3, 110)]
    [InlineData(99, 220)]
    public void OutOfRangeSteps_AreClamped(int step, int width) =>
        CardSizes.WidthOf(step).Should().Be(width);

    [Fact]
    public void Justified_FillsWidthExactly()
    {
        var (columns, gap) = JustifiedLayout.Compute(availableWidth: 1000, itemWidth: 150, minGap: 12);

        columns.Should().Be(6);                       // 6×150 + 5×12 = 960 ≤ 1000; a 7th does not fit
        (columns * 150 + (columns - 1) * gap).Should().BeApproximately(1000, 1e-9);
        gap.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void Justified_ExactFit_UsesMinimumGap()
    {
        var (columns, gap) = JustifiedLayout.Compute(availableWidth: 960, itemWidth: 150, minGap: 12);

        columns.Should().Be(6);
        gap.Should().BeApproximately(12, 1e-9);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(250)]
    [InlineData(double.PositiveInfinity)]
    public void Justified_TooNarrowOrUnbounded_FallsBackToSingleColumn(double width)
    {
        JustifiedLayout.Compute(width, itemWidth: 150, minGap: 12).Should().Be((1, 0.0));
    }
}
