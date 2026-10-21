namespace WindowWrangler.Core;

[Flags]
public enum ZoneEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

// Free-floating zones that may overlap, like FancyZones' canvas. Unlike FancyZones these are
// stored on the 0..10000 scale rather than in pixels, so the layout fits any monitor.
public sealed class CanvasLayout : ZoneLayout
{
    public List<Rect> Zones { get; set; } = [new(2500, 2500, 7500, 7500)];

    public override IReadOnlyList<Rect> GetZones(Rect workArea) =>
        Zones.Select(z => new Rect(
            workArea.Left + Scale(z.Left, workArea.Width), workArea.Top + Scale(z.Top, workArea.Height),
            workArea.Left + Scale(z.Right, workArea.Width), workArea.Top + Scale(z.Bottom, workArea.Height)))
        .ToList();

    static int Scale(int value, int length) => (int)((long)length * value / Multiplier);

    public override bool IsValid(out string? error)
    {
        error = Zones is null || Zones.Count == 0 ? "Canvas needs at least one zone."
            : Zones.Any(z => z.Left < 0 || z.Top < 0 || z.Right > Multiplier || z.Bottom > Multiplier) ? "Canvas zones must fit on the screen."
            : Zones.Any(z => z.Width < MinPercent || z.Height < MinPercent) ? "Canvas zones are too small."
            : null;
        return error is null;
    }

    public override CanvasLayout Clone() => new() { Id = Id, Name = Name, QuickKey = QuickKey, Zones = [.. Zones] };

    // New zones land in the middle, stepped so they don't hide exactly behind the last one.
    public int AddZone()
    {
        int step = Zones.Count % 6 * 300;
        Zones.Add(new Rect(3000 + step, 3000 + step, 6500 + step, 6500 + step));
        return Zones.Count - 1;
    }

    // Keeps at least one zone; a canvas with none is useless.
    public bool RemoveZone(int index)
    {
        if (Zones.Count <= 1 || index < 0 || index >= Zones.Count) return false;
        Zones.RemoveAt(index);
        return true;
    }

    // Moves a zone from where it started by dx/dy, keeping it on screen.
    public void Move(int index, Rect start, int dx, int dy)
    {
        int left = Math.Clamp(start.Left + dx, 0, Multiplier - start.Width);
        int top = Math.Clamp(start.Top + dy, 0, Multiplier - start.Height);
        Zones[index] = new Rect(left, top, left + start.Width, top + start.Height);
    }

    // Drags the given edges of a zone from where it started, keeping it on screen and not tiny.
    public void Resize(int index, Rect start, ZoneEdges edges, int dx, int dy)
    {
        int l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        if (edges.HasFlag(ZoneEdges.Left)) l = Math.Clamp(l + dx, 0, r - MinPercent);
        if (edges.HasFlag(ZoneEdges.Right)) r = Math.Clamp(r + dx, l + MinPercent, Multiplier);
        if (edges.HasFlag(ZoneEdges.Top)) t = Math.Clamp(t + dy, 0, b - MinPercent);
        if (edges.HasFlag(ZoneEdges.Bottom)) b = Math.Clamp(b + dy, t + MinPercent, Multiplier);
        Zones[index] = new Rect(l, t, r, b);
    }
}
