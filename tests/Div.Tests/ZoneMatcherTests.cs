using Div.Core;

namespace Div.Tests;

public class ZoneMatcherTests
{
    static readonly IReadOnlyList<Rect> ThreeColumns = GridLayout.CreateColumns(3).GetZones(new Rect(0, 0, 3000, 1000));

    [Fact]
    public void WindowWithinSnapDistance_IsCorrected()
    {
        // The Edge special: right zone, but a few pixels short of filling it.
        var window = new Rect(2008, 0, 2995, 992);

        Assert.Equal(ThreeColumns[2], ZoneMatcher.FindCorrection(window, ThreeColumns, 10, 2));
    }

    [Fact]
    public void WindowFurtherOffThanSnapDistance_IsLeftAlone()
    {
        // 60 pixels short at the bottom.
        var window = new Rect(2008, 0, 2990, 940);

        Assert.Null(ZoneMatcher.FindCorrection(window, ThreeColumns, 10, 2));
        Assert.Equal(ThreeColumns[2], ZoneMatcher.FindCorrection(window, ThreeColumns, 60, 2));
    }

    [Fact]
    public void WindowAlreadyInPlace_IsLeftAlone()
    {
        var window = new Rect(1001, 0, 2000, 999);

        Assert.Null(ZoneMatcher.FindCorrection(window, ThreeColumns, 10, 2));
    }

    [Fact]
    public void FloatingWindow_IsLeftAlone()
    {
        var window = new Rect(1100, 300, 1900, 700);

        Assert.Null(ZoneMatcher.FindCorrection(window, ThreeColumns, 10, 2));
    }

    [Fact]
    public void WindowAcrossTwoZones_SnapsToBoth()
    {
        var window = new Rect(6, 4, 1994, 996);

        Assert.Equal(new Rect(0, 0, 2000, 1000), ZoneMatcher.FindCorrection(window, ThreeColumns, 10, 2));
    }

    [Fact]
    public void SnapDistanceOfZero_NeverMovesAnything()
    {
        Assert.Null(ZoneMatcher.FindCorrection(new Rect(1003, 0, 2000, 1000), ThreeColumns, 0, 0));
    }

    [Fact]
    public void ClosestZoneWins()
    {
        // Within 10 of the middle column and nowhere near anything else.
        Assert.Equal(ThreeColumns[1], ZoneMatcher.FindNear(new Rect(1004, 3, 1996, 1000), ThreeColumns, 10));
    }

    [Fact]
    public void BestOverlap_FindsWhereARememberedSpotWent()
    {
        // The old layout's middle zone, now that the layout has slightly different columns.
        var edited = new GridLayout { ColumnPercents = [3000, 4000, 3000], CellMap = [[0, 1, 2]] }.GetZones(new Rect(0, 0, 3000, 1000));

        Assert.Equal(edited[1], ZoneMatcher.BestOverlap(new Rect(1000, 0, 2000, 1000), edited, 0.6));
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
