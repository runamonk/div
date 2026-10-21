namespace WindowWrangler.Core;

public static class ZoneMatcher
{
    // Single zones plus pairs of neighbours, so a window deliberately spread over two zones
    // isn't squashed into one. A pair only counts if no third zone sits inside its bounding box.
    public static List<Rect> Candidates(IReadOnlyList<Rect> zones)
    {
        var candidates = new List<Rect>(zones);
        for (int i = 0; i < zones.Count; i++)
            for (int j = i + 1; j < zones.Count; j++)
            {
                var union = zones[i].Union(zones[j]);
                bool blocked = false;
                for (int k = 0; k < zones.Count && !blocked; k++)
                    blocked = k != i && k != j && union.Intersect(zones[k]).Area > 0;
                if (!blocked) candidates.Add(union);
            }
        return candidates;
    }

    // The candidate the window overlaps best, or null if nothing reaches the threshold.
    public static Rect? FindBest(Rect window, IReadOnlyList<Rect> zones, double threshold)
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

    // Where the window should move to, or null if it's floating or already in place.
    public static Rect? FindCorrection(Rect window, IReadOnlyList<Rect> zones, double threshold, int tolerance)
    {
        var best = FindBest(window, zones, threshold);
        return best is { } target && !target.NearlyEquals(window, tolerance) ? target : null;
    }
}
