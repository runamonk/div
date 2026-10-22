namespace WindowWrangler.Core;

public enum Direction { Left, Up, Right, Down }

public static class ZoneMatcher
{
    // A window this close (in pixels) to a zone counts as being in it: not worth moving, since apps
    // round their own sizes by a pixel or two and we'd only fight them.
    public const int Tolerance = 2;

    // Single zones plus pairs of neighbours, so a window deliberately spread over two zones
    // isn't squashed into one. A pair only counts if no third zone sits inside its bounding box.
    public static List<Rect> Candidates(IReadOnlyList<Rect> zones)
    {
        var candidates = new List<Rect>(zones);
        for (int i = 0; i < zones.Count; i++)
            for (int j = i + 1; j < zones.Count; j++)
                if (CanPair(zones, i, j)) candidates.Add(zones[i].Union(zones[j]));
        return candidates;
    }

    static bool CanPair(IReadOnlyList<Rect> zones, int i, int j)
    {
        var union = zones[i].Union(zones[j]);
        for (int k = 0; k < zones.Count; k++)
            if (k != i && k != j && union.Intersect(zones[k]).Area > 0) return false;
        return true;
    }

    // The zone (or pair) whose edges are all within maxDistance pixels of the window's, closest first.
    // Null if nothing is that close.
    public static Rect? FindNear(Rect window, IReadOnlyList<Rect> zones, int maxDistance)
    {
        Rect? best = null;
        int bestDistance = maxDistance;
        foreach (var candidate in Candidates(zones))
        {
            int distance = window.EdgeDistance(candidate);
            if (distance <= bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }
        return best;
    }

    // The zone (or pair) the window overlaps most, if it overlaps at least that much (0..1). Looser
    // than FindNear: for finding where a remembered spot went after the layout was edited.
    public static Rect? BestOverlap(Rect window, IReadOnlyList<Rect> zones, double threshold)
    {
        Rect? best = null;
        double bestScore = threshold;
        foreach (var candidate in Candidates(zones))
        {
            double score = window.OverlapRatio(candidate);
            if (score >= bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best;
    }

    // What a dragged window would drop into with the cursor at x,y: the zone under it, or that zone
    // plus a neighbour when the cursor is within reach of the edge they share. Null in no-man's land.
    public static Rect? TargetAt(int x, int y, IReadOnlyList<Rect> zones, int reach)
    {
        // Last wins, so on a canvas the zone drawn on top is picked.
        int under = -1;
        for (int i = 0; i < zones.Count; i++)
            if (Distance(zones[i], x, y) == 0) under = i;
        if (under < 0) return null;

        // A neighbour counts only if the cursor is outside it but close; overlapping canvas zones
        // the cursor is inside both of shouldn't merge.
        int partner = -1;
        int partnerDistance = int.MaxValue;
        for (int j = 0; j < zones.Count; j++)
        {
            int distance = Distance(zones[j], x, y);
            if (j == under || distance == 0 || distance > reach || distance >= partnerDistance) continue;
            if (!CanPair(zones, under, j)) continue;
            partner = j;
            partnerDistance = distance;
        }
        return partner >= 0 ? zones[under].Union(zones[partner]) : zones[under];
    }

    // How far a point is outside a rectangle; 0 inside.
    static int Distance(Rect r, int x, int y)
    {
        int dx = Math.Max(Math.Max(r.Left - x, 0), x - (r.Right - 1));
        int dy = Math.Max(Math.Max(r.Top - y, 0), y - (r.Bottom - 1));
        return Math.Max(dx, dy);
    }

    // The zone next to the window in a direction, for Win+arrow. Zones that line up with the window
    // across the direction win over ones that are merely closer. Null if there's nothing that way.
    public static Rect? Neighbour(Rect window, IReadOnlyList<Rect> zones, Direction direction, int tolerance)
    {
        double cx = (window.Left + window.Right) / 2.0, cy = (window.Top + window.Bottom) / 2.0;
        Rect? best = null;
        (bool Overlaps, double Distance) bestScore = (false, double.MaxValue);

        foreach (var zone in zones)
        {
            // The zone the window is already in (snapped or floating inside it) isn't "that way".
            if (zone.NearlyEquals(window, tolerance) || Distance(zone, (int)cx, (int)cy) == 0) continue;
            double zx = (zone.Left + zone.Right) / 2.0, zy = (zone.Top + zone.Bottom) / 2.0;
            double ahead = direction switch
            {
                Direction.Left => cx - zx,
                Direction.Right => zx - cx,
                Direction.Up => cy - zy,
                _ => zy - cy,
            };
            if (ahead <= 1) continue;

            bool across = direction is Direction.Left or Direction.Right;
            bool overlaps = across
                ? Math.Min(window.Bottom, zone.Bottom) > Math.Max(window.Top, zone.Top)
                : Math.Min(window.Right, zone.Right) > Math.Max(window.Left, zone.Left);
            double sideways = across ? Math.Abs(zy - cy) : Math.Abs(zx - cx);
            // Lined-up zones are judged on distance ahead alone; the rest also pay for drifting sideways.
            var score = (overlaps, overlaps ? ahead : ahead + sideways);

            bool better = score.overlaps != bestScore.Overlaps ? score.overlaps : score.Item2 < bestScore.Distance;
            if (best is null || better)
            {
                best = zone;
                bestScore = score;
            }
        }
        return best;
    }

    // The zone the maximize button should fill: the one under the window's middle, or the zone
    // (or pair) it already fills, which keeps it there. Null when it isn't over a zone at all,
    // so the click goes through as a normal maximize.
    public static Rect? MaximizeTarget(Rect window, IReadOnlyList<Rect> zones, int tolerance)
    {
        if (Candidates(zones).FirstOrDefault(c => c.NearlyEquals(window, tolerance)) is { Area: > 0 } filled) return filled;
        return TargetAt((window.Left + window.Right) / 2, (window.Top + window.Bottom) / 2, zones, reach: 0);
    }

    // Where the window should move to, or null if it's floating or already in place.
    public static Rect? FindCorrection(Rect window, IReadOnlyList<Rect> zones, int maxDistance, int tolerance)
    {
        var best = FindNear(window, zones, maxDistance);
        return best is { } target && !target.NearlyEquals(window, tolerance) ? target : null;
    }
}
