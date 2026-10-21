using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public class GridLayoutTests
{
    static readonly Rect Screen = new(0, 0, 3000, 1000);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Columns_AddUpExactly(int count)
    {
        var layout = GridLayout.CreateColumns(count);

        Assert.True(layout.IsValid(out _));
        Assert.Equal(GridLayout.Multiplier, layout.ColumnPercents.Sum());
    }

    [Fact]
    public void ThreeColumns_SplitTheScreenIntoThirds()
    {
        var zones = GridLayout.CreateColumns(3).GetZones(Screen);

        Assert.Equal([new Rect(0, 0, 1000, 1000), new Rect(1000, 0, 2000, 1000), new Rect(2000, 0, 3000, 1000)], zones);
    }

    [Fact]
    public void Zones_AreOffsetByTheWorkArea()
    {
        var zones = GridLayout.CreateColumns(2).GetZones(new Rect(-1920, 40, 0, 1080));

        Assert.Equal([new Rect(-1920, 40, -960, 1080), new Rect(-960, 40, 0, 1080)], zones);
    }

    [Fact]
    public void Spacing_IsFullOnOuterEdgesAndSplitBetweenZones()
    {
        var layout = GridLayout.CreateColumns(2);
        layout.Spacing = 10;

        var zones = layout.GetZones(new Rect(0, 0, 2000, 1000));

        Assert.Equal([new Rect(10, 10, 995, 990), new Rect(1005, 10, 1990, 990)], zones);
    }

    [Fact]
    public void SpanningZone_CoversAllItsCells()
    {
        // Big zone on the left, two stacked on the right.
        var layout = new GridLayout
        {
            RowPercents = [5000, 5000],
            ColumnPercents = [5000, 5000],
            CellMap = [[0, 1], [0, 2]],
        };

        Assert.True(layout.IsValid(out _));
        Assert.Equal([new Rect(0, 0, 1500, 1000), new Rect(1500, 0, 3000, 500), new Rect(1500, 500, 3000, 1000)],
            layout.GetZones(Screen));
    }

    [Fact]
    public void NonRectangularZone_IsInvalid()
    {
        var layout = new GridLayout
        {
            RowPercents = [5000, 5000],
            ColumnPercents = [5000, 5000],
            CellMap = [[0, 0], [0, 1]],
        };

        Assert.False(layout.IsValid(out var error));
        Assert.Contains("rectangle", error);
    }

    [Fact]
    public void PercentsThatDontAddUp_AreInvalid()
    {
        var layout = new GridLayout { ColumnPercents = [5000, 4000], CellMap = [[0, 1]] };

        Assert.False(layout.IsValid(out _));
    }

    [Fact]
    public void MissingZoneNumber_IsInvalid()
    {
        var layout = new GridLayout { ColumnPercents = [5000, 5000], CellMap = [[0, 2]] };

        Assert.False(layout.IsValid(out _));
    }
}
