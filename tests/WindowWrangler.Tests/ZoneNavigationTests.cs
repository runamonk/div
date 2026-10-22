using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public class ZoneNavigationTests
{
    static readonly IReadOnlyList<Rect> ThreeColumns = GridLayout.CreateColumns(3).GetZones(new Rect(0, 0, 3000, 1000));
    // Left column, right column split top/bottom.
    static readonly IReadOnlyList<Rect> Stacked = new GridLayout
    {
        RowPercents = [5000, 5000],
        ColumnPercents = [5000, 5000],
        CellMap = [[0, 1], [0, 2]],
    }.GetZones(new Rect(0, 0, 2000, 1000));

    [Fact]
    public void CursorInAZone_TargetsThatZone()
    {
        Assert.Equal(ThreeColumns[1], ZoneMatcher.TargetAt(1500, 500, ThreeColumns, 20));
    }

    [Fact]
    public void CursorNearASharedEdge_TargetsBothZones()
    {
        Assert.Equal(new Rect(1000, 0, 3000, 1000), ZoneMatcher.TargetAt(1990, 500, ThreeColumns, 20));
    }

    [Fact]
    public void ReachOfZero_NeverSpans()
    {
        Assert.Equal(ThreeColumns[1], ZoneMatcher.TargetAt(1999, 500, ThreeColumns, 0));
    }

    [Fact]
    public void EdgeWhereThePairWouldSwallowAThirdZone_TargetsOneZone()
    {
        // Left column's edge, beside the top-right zone; pairing them would cover bottom-right too.
        Assert.Equal(Stacked[0], ZoneMatcher.TargetAt(995, 200, Stacked, 20));
    }

    [Fact]
    public void OverlappingCanvasZones_DontMergeJustBecauseTheCursorIsInBoth()
    {
        var zones = new CanvasLayout { Zones = [new(0, 0, 6000, 10000), new(4000, 0, 10000, 10000)] }
            .GetZones(new Rect(0, 0, 1000, 1000));

        Assert.Equal(zones[1], ZoneMatcher.TargetAt(500, 500, zones, 20));
    }

    [Fact]
    public void CursorInAGap_TargetsNothing()
    {
        var layout = GridLayout.CreateColumns(2);
        layout.Spacing = 40;
        var zones = layout.GetZones(new Rect(0, 0, 2000, 1000));

        Assert.Null(ZoneMatcher.TargetAt(1000, 500, zones, 0));
    }

    [Fact]
    public void Maximize_FillsTheZoneTheWindowIsIn()
    {
        var floating = new Rect(1200, 300, 1700, 700);

        Assert.Equal(ThreeColumns[1], ZoneMatcher.MaximizeTarget(floating, ThreeColumns, 2));
    }

    [Fact]
    public void Maximize_KeepsAWindowThatAlreadyFillsItsZoneThere()
    {
        // Only Shift+click goes full screen.
        Assert.Equal(ThreeColumns[1], ZoneMatcher.MaximizeTarget(new Rect(1001, 0, 2000, 999), ThreeColumns, 2));
        // Spread over two zones stays spread, rather than shrinking to one.
        Assert.Equal(new Rect(0, 0, 2000, 1000), ZoneMatcher.MaximizeTarget(new Rect(0, 0, 2000, 1000), ThreeColumns, 2));
    }

    [Fact]
    public void Maximize_GoesThroughWhenTheWindowIsntOverAZone()
    {
        var layout = GridLayout.CreateColumns(2);
        layout.Spacing = 100;
        var zones = layout.GetZones(new Rect(0, 0, 2000, 1000));

        // Centred on the gap between the two zones.
        Assert.Null(ZoneMatcher.MaximizeTarget(new Rect(800, 300, 1200, 700), zones, 2));
    }

    [Theory]
    [InlineData(Direction.Right, 2)]
    [InlineData(Direction.Left, 0)]
    public void Arrows_MoveToTheNextColumn(Direction direction, int expected)
    {
        Assert.Equal(ThreeColumns[expected], ZoneMatcher.Neighbour(ThreeColumns[1], ThreeColumns, direction, 2));
    }

    [Theory]
    [InlineData(Direction.Up)]
    [InlineData(Direction.Down)]
    public void NothingThatWay_ReturnsNull(Direction direction)
    {
        // So Windows gets Win+Up/Down back in a columns layout.
        Assert.Null(ZoneMatcher.Neighbour(ThreeColumns[1], ThreeColumns, direction, 2));
    }

    [Fact]
    public void EdgeOfTheMonitor_ReturnsNull()
    {
        Assert.Null(ZoneMatcher.Neighbour(ThreeColumns[2], ThreeColumns, Direction.Right, 2));
    }

    [Fact]
    public void Down_GoesToTheZoneBelow()
    {
        Assert.Equal(Stacked[2], ZoneMatcher.Neighbour(Stacked[1], Stacked, Direction.Down, 2));
    }

    [Fact]
    public void LinedUpZone_BeatsACloserOneOffToTheSide()
    {
        // From the bottom-right zone, Left should land on the tall left zone it lines up with.
        Assert.Equal(Stacked[0], ZoneMatcher.Neighbour(Stacked[2], Stacked, Direction.Left, 2));
    }

    [Fact]
    public void ZonesOnTheNextMonitor_AreReachable()
    {
        var both = ThreeColumns.Concat(GridLayout.CreateColumns(2).GetZones(new Rect(3000, 0, 5000, 1000))).ToList();

        Assert.Equal(new Rect(3000, 0, 4000, 1000), ZoneMatcher.Neighbour(ThreeColumns[2], both, Direction.Right, 2));
    }

    [Fact]
    public void FloatingWindow_GoesToTheNearestZoneThatWay()
    {
        var window = new Rect(1200, 300, 1700, 700);

        Assert.Equal(ThreeColumns[2], ZoneMatcher.Neighbour(window, ThreeColumns, Direction.Right, 2));
    }
}
