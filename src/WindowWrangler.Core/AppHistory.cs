using System.Text.Json;

namespace WindowWrangler.Core;

public sealed record Placement(string MonitorId, Rect Frame);

// Where each app's windows last sat in a zone, newest first. Lets us put back apps
// that don't remember their own position. Keeps a few per app, so two browser windows
// can each go back to their own spot.
public sealed class AppHistory
{
    public const int MaxPerApp = 4;
    // Close enough to count as the same spot.
    const int SameTolerance = 8;

    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Dictionary<string, List<Placement>> Apps { get; set; } = [];

    // replaces: where this same window was before, so moving it doesn't leave a stale entry.
    public void Record(string app, Placement placement, Placement? replaces = null)
    {
        if (!Apps.TryGetValue(app, out var list)) Apps[app] = list = [];
        if (replaces is not null) list.RemoveAll(p => Same(p, replaces));
        list.RemoveAll(p => Same(p, placement));
        list.Insert(0, placement);
        if (list.Count > MaxPerApp) list.RemoveRange(MaxPerApp, list.Count - MaxPerApp);
    }

    public void Forget(string app, Placement placement)
    {
        if (!Apps.TryGetValue(app, out var list)) return;
        list.RemoveAll(p => Same(p, placement));
        if (list.Count == 0) Apps.Remove(app);
    }

    // The newest spot that no other window of the app is already sitting in.
    public Placement? Pick(string app, IEnumerable<Placement> taken) =>
        Apps.TryGetValue(app, out var list) ? list.FirstOrDefault(p => !taken.Any(t => Same(t, p))) : null;

    static bool Same(Placement a, Placement b) =>
        a.MonitorId == b.MonitorId && a.Frame.NearlyEquals(b.Frame, SameTolerance);

    // History is disposable: missing or broken just means starting fresh.
    public static AppHistory Load(string path)
    {
        try
        {
            var history = File.Exists(path) ? JsonSerializer.Deserialize<AppHistory>(File.ReadAllText(path), JsonOptions) : null;
            if (history?.Apps is null) return new AppHistory();
            foreach (var key in history.Apps.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList())
                history.Apps.Remove(key);
            return history;
        }
        catch (JsonException)
        {
            return new AppHistory();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }
}
