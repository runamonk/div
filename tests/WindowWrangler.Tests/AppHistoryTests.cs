using WindowWrangler.Core;

namespace WindowWrangler.Tests;

public sealed class AppHistoryTests : IDisposable
{
    const string Chrome = "chrome|Chrome_WidgetWin_1";
    static readonly Placement Left = new("m1", new Rect(0, 0, 1000, 1000));
    static readonly Placement Right = new("m1", new Rect(1000, 0, 2000, 1000));
    static readonly Placement OtherMonitor = new("m2", new Rect(0, 0, 1000, 1000));

    readonly string _folder = Path.Combine(Path.GetTempPath(), "WindowWranglerTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void NewestPlacementComesFirst()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);
        history.Record(Chrome, Right);

        Assert.Equal(Right, history.Pick(Chrome, []));
    }

    [Fact]
    public void SecondWindow_GetsTheOtherSpot()
    {
        // Main browser on the left, video window on the right.
        var history = new AppHistory();
        history.Record(Chrome, Left);
        history.Record(Chrome, Right);

        Assert.Equal(Left, history.Pick(Chrome, [Right with { Frame = new Rect(1003, 0, 2000, 1000) }]));
    }

    [Fact]
    public void AllSpotsTaken_PicksNothing()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);

        Assert.Null(history.Pick(Chrome, [Left]));
        Assert.Null(history.Pick("unknown", []));
    }

    [Fact]
    public void MovingAWindow_ReplacesItsOldSpot()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);

        history.Record(Chrome, Right, replaces: Left);

        Assert.Equal([Right], history.Apps[Chrome]);
    }

    [Fact]
    public void SameSpotTwice_IsKeptOnce()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);
        history.Record(Chrome, Left with { Frame = new Rect(2, 0, 1000, 1000) });

        Assert.Single(history.Apps[Chrome]);
    }

    [Fact]
    public void SameRectOnAnotherMonitor_IsADifferentSpot()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);
        history.Record(Chrome, OtherMonitor);

        Assert.Equal(2, history.Apps[Chrome].Count);
    }

    [Fact]
    public void OnlyTheLastFewAreKept()
    {
        var history = new AppHistory();
        for (int i = 0; i < 10; i++)
            history.Record(Chrome, new Placement("m1", new Rect(i * 100, 0, i * 100 + 500, 500)));

        Assert.Equal(AppHistory.MaxPerApp, history.Apps[Chrome].Count);
        Assert.Equal(900, history.Apps[Chrome][0].Frame.Left);
    }

    [Fact]
    public void Forget_RemovesTheSpotAndEmptyApps()
    {
        var history = new AppHistory();
        history.Record(Chrome, Left);

        history.Forget(Chrome, Left);

        Assert.Empty(history.Apps);
    }

    [Fact]
    public void SavedHistory_RoundTrips()
    {
        var path = Path.Combine(_folder, "history.json");
        var history = new AppHistory();
        history.Record(Chrome, Left);
        history.Record(Chrome, Right);
        history.Save(path);

        var loaded = AppHistory.Load(path);

        Assert.Equal([Right, Left], loaded.Apps[Chrome]);
    }

    [Fact]
    public void BrokenFile_StartsFresh()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, "history.json");
        File.WriteAllText(path, "{ nope");

        Assert.Empty(AppHistory.Load(path).Apps);
    }
}
