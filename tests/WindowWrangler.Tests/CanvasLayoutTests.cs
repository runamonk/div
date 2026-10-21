using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public class CanvasLayoutTests
{
    [Fact]
    public void Zones_ScaleToTheWorkArea()
    {
        var layout = new CanvasLayout { Zones = [new(0, 0, 5000, 10000), new(2500, 2500, 7500, 7500)] };

        var zones = layout.GetZones(new Rect(100, 50, 2100, 1050));

        Assert.Equal([new Rect(100, 50, 1100, 1050), new Rect(600, 300, 1600, 800)], zones);
    }

    [Fact]
    public void OverlappingZones_AreFine()
    {
        var layout = new CanvasLayout { Zones = [new(0, 0, 6000, 6000), new(4000, 4000, 10000, 10000)] };

        Assert.True(layout.IsValid(out _));
    }

    [Fact]
    public void OffScreenOrEmpty_IsInvalid()
    {
        Assert.False(new CanvasLayout { Zones = [] }.IsValid(out _));
        Assert.False(new CanvasLayout { Zones = [new(-10, 0, 5000, 5000)] }.IsValid(out _));
        Assert.False(new CanvasLayout { Zones = [new(0, 0, 100, 5000)] }.IsValid(out _));
    }

    [Fact]
    public void Move_StaysOnScreenAndKeepsItsSize()
    {
        var layout = new CanvasLayout();
        var start = layout.Zones[0];

        layout.Move(0, start, 9000, -9000);

        Assert.Equal(new Rect(5000, 0, 10000, 5000), layout.Zones[0]);
    }

    [Fact]
    public void Resize_MovesOnlyTheDraggedEdges()
    {
        var layout = new CanvasLayout();

        layout.Resize(0, layout.Zones[0], ZoneEdges.Right | ZoneEdges.Bottom, 1000, 500);

        Assert.Equal(new Rect(2500, 2500, 8500, 8000), layout.Zones[0]);
    }

    [Fact]
    public void Resize_WontCollapseAZone()
    {
        var layout = new CanvasLayout();

        layout.Resize(0, layout.Zones[0], ZoneEdges.Left, 9000, 0);

        Assert.Equal(ZoneLayout.MinPercent, layout.Zones[0].Width);
    }

    [Fact]
    public void LastZone_CantBeRemoved()
    {
        var layout = new CanvasLayout();
        int added = layout.AddZone();

        Assert.True(layout.RemoveZone(added));
        Assert.False(layout.RemoveZone(0));
        Assert.Single(layout.Zones);
    }

    [Fact]
    public void AddedZones_DontStackExactly()
    {
        var layout = new CanvasLayout();
        layout.AddZone();
        layout.AddZone();

        Assert.NotEqual(layout.Zones[1], layout.Zones[2]);
        Assert.True(layout.IsValid(out _));
    }
}
