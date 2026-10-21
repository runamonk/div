using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public class ZoneMatcherTests
{
    static readonly IReadOnlyList<Rect> ThreeColumns = GridLayout.CreateColumns(3).GetZones(new Rect(0, 0, 3000, 1000));

    [Fact]
    public void WindowNearlyInAZone_IsCorrected()
    {
        // The Edge special: right zone, but not quite filling it.
        var window = new Rect(2008, 0, 2990, 940);

        Assert.Equal(ThreeColumns[2], ZoneMatcher.FindCorrection(window, ThreeColumns, 0.6, 2));
    }

    [Fact]
    public void WindowAlreadyInPlace_IsLeftAlone()
    {
        var window = new Rect(1001, 0, 2000, 999);

        Assert.Null(ZoneMatcher.FindCorrection(window, ThreeColumns, 0.6, 2));
    }

    [Fact]
    public void FloatingWindow_IsLeftAlone()
    {
        var window = new Rect(1100, 300, 1900, 700);

        Assert.Null(ZoneMatcher.FindCorrection(window, ThreeColumns, 0.6, 2));
    }

    [Fact]
    public void WindowAcrossTwoZones_SnapsToBoth()
    {
        var window = new Rect(20, 10, 1980, 990);

        Assert.Equal(new Rect(0, 0, 2000, 1000), ZoneMatcher.FindCorrection(window, ThreeColumns, 0.6, 2));
    }

    [Fact]
    public void Candidates_SkipPairsWithAZoneInBetween()
    {
        var candidates = ZoneMatcher.Candidates(ThreeColumns);

        // 3 singles + left/middle + middle/right; left/right would swallow the middle.
        Assert.Equal(5, candidates.Count);
        Assert.DoesNotContain(new Rect(0, 0, 3000, 1000), candidates);
    }

    [Fact]
    public void Candidates_SkipDiagonalPairs()
    {
        var grid = new GridLayout
        {
            RowPercents = [5000, 5000],
            ColumnPercents = [5000, 5000],
            CellMap = [[0, 1], [2, 3]],
        }.GetZones(new Rect(0, 0, 1000, 1000));

        // 4 singles + 2 horizontal pairs + 2 vertical pairs.
        Assert.Equal(8, ZoneMatcher.Candidates(grid).Count);
    }
}
