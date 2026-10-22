using Div.Core;

namespace Div.Tests;

public class GridEditingTests
{
    static readonly Rect Screen = new(0, 0, 1000, 1000);

    [Fact]
    public void VerticalSplit_PutsHalvesSideBySide()
    {
        var layout = new GridLayout();

        Assert.True(layout.Split(0, 3000, vertical: true));

        Assert.True(layout.IsValid(out _));
        Assert.Equal([new Rect(0, 0, 300, 1000), new Rect(300, 0, 1000, 1000)], layout.GetZones(Screen));
    }

    [Fact]
    public void HorizontalSplit_StacksHalves()
    {
        var layout = new GridLayout();

        Assert.True(layout.Split(0, 4000, vertical: false));

        Assert.True(layout.IsValid(out _));
        Assert.Equal([new Rect(0, 0, 1000, 400), new Rect(0, 400, 1000, 1000)], layout.GetZones(Screen));
    }

    [Fact]
    public void SplittingOneZone_LeavesItsNeighboursWhole()
    {
        // Two stacked rows; cut only the bottom one.
        var layout = GridLayout.CreateRows(2);

        Assert.True(layout.Split(1, 5000, vertical: true));

        Assert.True(layout.IsValid(out _));
        Assert.Equal(3, layout.ZoneCount);
        Assert.Equal(new Rect(0, 0, 1000, 500), layout.GetZones(Screen)[0]);
    }

    [Fact]
    public void SplitNearAnExistingLine_ReusesIt()
    {
        // Top row is split at 50%; splitting the bottom near 50% shouldn't add a sliver column.
        var layout = GridLayout.CreateRows(2);
        layout.Split(0, 5000, vertical: true);

        layout.Split(2, 5100, vertical: true);

        Assert.Equal(2, layout.Columns);
        Assert.Equal(4, layout.ZoneCount);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(9900)]
    public void SplitTooCloseToTheEdge_IsRefused(int position)
    {
        var layout = new GridLayout();

        Assert.False(layout.Split(0, position, vertical: true));
        Assert.Equal(1, layout.ZoneCount);
    }

    [Fact]
    public void Merge_TwoNeighbours_BecomeOneZone()
    {
        var layout = GridLayout.CreateColumns(3);

        layout.Merge([0, 1]);

        Assert.True(layout.IsValid(out _));
        Assert.Equal(2, layout.ZoneCount);
        // The redundant column is gone too.
        Assert.Equal(2, layout.Columns);
    }

    [Fact]
    public void Merge_PullsInWhateverIsInTheWay()
    {
        var layout = GridLayout.CreateColumns(3);

        Assert.Equal([0, 1, 2], layout.MergeCover([0, 2]).Order());
        layout.Merge([0, 2]);

        Assert.Equal(1, layout.ZoneCount);
    }

    [Fact]
    public void Merge_DiagonalCornersOfAGrid_TakesEverything()
    {
        var layout = GridLayout.CreateGrid(2, 2);

        layout.Merge([0, 3]);

        Assert.Equal(1, layout.ZoneCount);
        Assert.Equal(1, layout.Rows);
        Assert.Equal(1, layout.Columns);
    }

    [Fact]
    public void Merge_GrowsToCoverZonesThatStickOut()
    {
        // Left column spans both rows; merging it with the top right must take the bottom right too.
        var layout = new GridLayout { RowPercents = [5000, 5000], ColumnPercents = [5000, 5000], CellMap = [[0, 1], [0, 2]] };

        Assert.Equal([1], layout.MergeCover([1]));
        Assert.Equal([1, 2], layout.MergeCover([1, 2]).Order());
        Assert.Equal([0, 1, 2], layout.MergeCover([0, 1]).Order());
    }

    [Fact]
    public void Zones_AreRenumberedLeftToRightTopToBottom()
    {
        var layout = new GridLayout();
        layout.Split(0, 5000, vertical: true);
        layout.Split(0, 5000, vertical: false);

        // Top-left, right, bottom-left.
        Assert.Equal([[0, 1], [2, 1]], layout.CellMap);
    }

    [Fact]
    public void MoveEdge_ResizesBothNeighbours()
    {
        var layout = GridLayout.CreateColumns(2);

        layout.MoveEdge(vertical: true, edge: 1, position: 7000);

        Assert.Equal([7000, 3000], layout.ColumnPercents);
    }

    [Fact]
    public void MoveEdge_StopsShortOfCrushingANeighbour()
    {
        var layout = GridLayout.CreateRows(2);

        layout.MoveEdge(vertical: false, edge: 1, position: 9999);

        Assert.Equal([GridLayout.Multiplier - GridLayout.MinPercent, GridLayout.MinPercent], layout.RowPercents);
    }

    [Fact]
    public void Dividers_OnlyCoverLinesBetweenDifferentZones()
    {
        // Left zone spans both rows, so the horizontal line only runs across the right column.
        var layout = new GridLayout { RowPercents = [5000, 5000], ColumnPercents = [5000, 5000], CellMap = [[0, 1], [0, 2]] };

        Assert.Equal([new Divider(true, 1, 0, 1), new Divider(false, 1, 1, 1)], layout.Dividers());
    }

    [Fact]
    public void Clone_IsIndependent()
    {
        var original = GridLayout.CreateColumns(2);
        var copy = original.Clone();

        copy.Split(0, 2500, vertical: true);

        Assert.Equal(2, original.ZoneCount);
        Assert.Equal(3, copy.ZoneCount);
    }

    [Fact]
    public void Templates_AreAllValid()
    {
        foreach (var template in LayoutTemplates.All)
            Assert.True(template.IsValid(out var error), $"{template.Name}: {error}");
    }
}
