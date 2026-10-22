using System.Text.Json.Serialization;

namespace Div.Core;

// A draggable line between zones. Vertical lines sit on column edge Edge and run over rows From..To;
// horizontal lines sit on row edge Edge and run over columns From..To.
public readonly record struct Divider(bool Vertical, int Edge, int From, int To);

// Same idea as FancyZones' grid: row/column sizes on a 0..10000 scale, plus a cell map
// where a zone that spans several cells repeats its index in each of them.
public sealed class GridLayout : ZoneLayout
{
    public int[] RowPercents { get; set; } = [Multiplier];
    public int[] ColumnPercents { get; set; } = [Multiplier];
    public int[][] CellMap { get; set; } = [[0]];
    // Gap in pixels around and between zones.
    public int Spacing { get; set; }

    [JsonIgnore] public int Rows => RowPercents.Length;
    [JsonIgnore] public int Columns => ColumnPercents.Length;
    [JsonIgnore] public int ZoneCount => CellMap.SelectMany(r => r).Max() + 1;

    public static GridLayout CreateColumns(int count) => new()
    {
        Name = $"{count} columns",
        ColumnPercents = Split(count),
        CellMap = [Enumerable.Range(0, count).ToArray()],
    };

    public static GridLayout CreateRows(int count)
    {
        var layout = CreateColumns(count).Transposed();
        layout.Name = $"{count} rows";
        return layout;
    }

    public static GridLayout CreateGrid(int rows, int columns) => new()
    {
        Name = $"{rows}x{columns} grid",
        RowPercents = Split(rows),
        ColumnPercents = Split(columns),
        CellMap = Enumerable.Range(0, rows).Select(r => Enumerable.Range(r * columns, columns).ToArray()).ToArray(),
    };

    // Splits Multiplier into count parts that add up exactly, so rounding never leaves a gap.
    static int[] Split(int count)
    {
        var parts = new int[count];
        for (int i = 0; i < count; i++)
            parts[i] = Multiplier / count + (i < Multiplier % count ? 1 : 0);
        return parts;
    }

    public override GridLayout Clone() => new()
    {
        Id = Id,
        Name = Name,
        QuickKey = QuickKey,
        RowPercents = [.. RowPercents],
        ColumnPercents = [.. ColumnPercents],
        CellMap = CellMap.Select(r => r.ToArray()).ToArray(),
        Spacing = Spacing,
    };

    // Rows become columns. Lets row operations reuse the column code.
    GridLayout Transposed() => new()
    {
        Id = Id,
        Name = Name,
        QuickKey = QuickKey,
        RowPercents = [.. ColumnPercents],
        ColumnPercents = [.. RowPercents],
        CellMap = Enumerable.Range(0, Columns).Select(c => Enumerable.Range(0, Rows).Select(r => CellMap[r][c]).ToArray()).ToArray(),
        Spacing = Spacing,
    };

    public override bool IsValid(out string? error)
    {
        error = Validate();
        return error is null;
    }

    string? Validate()
    {
        // Hand-edited JSON can null anything out.
        if (RowPercents is null || ColumnPercents is null || CellMap is null || CellMap.Any(r => r is null)) return "Layout is incomplete.";
        if (Rows == 0 || Columns == 0) return "Layout needs at least one row and one column.";
        if (RowPercents.Any(p => p < 1) || ColumnPercents.Any(p => p < 1)) return "Row and column sizes must be positive.";
        if (RowPercents.Sum() != Multiplier || ColumnPercents.Sum() != Multiplier) return $"Row and column sizes must each add up to {Multiplier}.";
        if (CellMap.Length != Rows || CellMap.Any(row => row.Length != Columns)) return "Cell map doesn't match the row and column counts.";
        if (CellMap.SelectMany(r => r).Any(z => z < 0)) return "Zone numbers can't be negative.";

        for (int zone = 0; zone < ZoneCount; zone++)
        {
            var (minR, minC, maxR, maxC, cells) = Bounds(zone);
            if (cells == 0) return $"Zone {zone} is missing; zone numbers must have no gaps.";
            if (cells != (maxR - minR + 1) * (maxC - minC + 1)) return $"Zone {zone} isn't a rectangle.";
        }
        return null;
    }

    (int MinR, int MinC, int MaxR, int MaxC, int Cells) Bounds(int zone)
    {
        int minR = int.MaxValue, minC = int.MaxValue, maxR = -1, maxC = -1, cells = 0;
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
            {
                if (CellMap[r][c] != zone) continue;
                cells++;
                minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                minC = Math.Min(minC, c); maxC = Math.Max(maxC, c);
            }
        return (minR, minC, maxR, maxC, cells);
    }

    // Assumes the layout is valid.
    public override IReadOnlyList<Rect> GetZones(Rect workArea)
    {
        var rowEdges = RowEdges(workArea.Top, workArea.Height);
        var colEdges = ColumnEdges(workArea.Left, workArea.Width);
        int s = Spacing, half = Spacing / 2;

        var zones = new List<Rect>(ZoneCount);
        for (int zone = 0; zone < ZoneCount; zone++)
        {
            var (minR, minC, maxR, maxC, _) = Bounds(zone);
            // Outer edges get the full gap, inner edges split it with the neighbour.
            zones.Add(new Rect(
                colEdges[minC] + (minC == 0 ? s : half),
                rowEdges[minR] + (minR == 0 ? s : half),
                colEdges[maxC + 1] - (maxC == Columns - 1 ? s : s - half),
                rowEdges[maxR + 1] - (maxR == Rows - 1 ? s : s - half)));
        }
        return zones;
    }

    // Pixel positions of the grid lines, including both outer edges.
    public int[] ColumnEdges(int start, int length) => Edges(ColumnPercents, start, length);
    public int[] RowEdges(int start, int length) => Edges(RowPercents, start, length);

    static int[] Edges(int[] percents, int start, int length)
    {
        var edges = new int[percents.Length + 1];
        long sum = 0;
        edges[0] = start;
        for (int i = 0; i < percents.Length; i++)
        {
            sum += percents[i];
            edges[i + 1] = start + (int)(length * sum / Multiplier);
        }
        return edges;
    }

    // Grid line positions on the 0..10000 scale.
    static int[] Starts(int[] percents)
    {
        var starts = new int[percents.Length + 1];
        for (int i = 0; i < percents.Length; i++)
            starts[i + 1] = starts[i] + percents[i];
        return starts;
    }

    // Cuts a zone in two at position (0..10000 across the layout). Vertical means a vertical line,
    // so the halves sit side by side. Returns false if the cut would leave a sliver.
    public bool Split(int zone, int position, bool vertical)
    {
        bool done;
        if (vertical)
            done = SplitColumns(zone, position);
        else
        {
            var t = Transposed();
            done = t.SplitColumns(zone, position);
            if (done) CopyGridFrom(t.Transposed());
        }
        if (done) Normalize();
        return done;
    }

    bool SplitColumns(int zone, int x)
    {
        var (minR, minC, maxR, maxC, cells) = Bounds(zone);
        if (cells == 0) return false;
        var starts = Starts(ColumnPercents);
        if (x < starts[minC] + MinPercent || x > starts[maxC + 1] - MinPercent) return false;

        // Close to a grid line already inside the zone? Use it instead of making a sliver column.
        int splitCol = -1;
        for (int c = minC + 1; c <= maxC && splitCol < 0; c++)
            if (Math.Abs(starts[c] - x) < MinPercent) splitCol = c;

        if (splitCol < 0)
        {
            int c = minC;
            while (starts[c + 1] <= x) c++;

            var percents = ColumnPercents.ToList();
            percents[c] = x - starts[c];
            percents.Insert(c + 1, starts[c + 1] - x);
            ColumnPercents = [.. percents];
            // Every other zone in this column just spans both halves.
            CellMap = CellMap.Select(row => row.Take(c + 1).Append(row[c]).Concat(row.Skip(c + 1)).ToArray()).ToArray();
            splitCol = c + 1;
            maxC++;
        }

        int newZone = ZoneCount;
        for (int r = minR; r <= maxR; r++)
            for (int c = splitCol; c <= maxC; c++)
                CellMap[r][c] = newZone;
        return true;
    }

    // The zones that merging these would swallow: the bounding box, grown until nothing sticks out of it.
    public HashSet<int> MergeCover(IEnumerable<int> zones)
    {
        var cover = Cover(zones);
        var result = new HashSet<int>();
        if (cover is not var (minR, minC, maxR, maxC)) return result;
        for (int r = minR; r <= maxR; r++)
            for (int c = minC; c <= maxC; c++)
                result.Add(CellMap[r][c]);
        return result;
    }

    // Turns the zones (and anything in between) into one rectangle. Returns the merged zone's number.
    public int Merge(IEnumerable<int> zones)
    {
        if (Cover(zones) is not var (minR, minC, maxR, maxC)) return -1;
        int target = CellMap[minR][minC];
        for (int r = minR; r <= maxR; r++)
            for (int c = minC; c <= maxC; c++)
                CellMap[r][c] = target;
        Normalize();
        return CellMap[minR][minC];
    }

    (int MinR, int MinC, int MaxR, int MaxC)? Cover(IEnumerable<int> zones)
    {
        int minR = int.MaxValue, minC = int.MaxValue, maxR = -1, maxC = -1;
        void Grow(int zone)
        {
            var b = Bounds(zone);
            if (b.Cells == 0) return;
            minR = Math.Min(minR, b.MinR); maxR = Math.Max(maxR, b.MaxR);
            minC = Math.Min(minC, b.MinC); maxC = Math.Max(maxC, b.MaxC);
        }

        foreach (var zone in zones) Grow(zone);
        if (maxR < 0) return null;

        while (true)
        {
            var before = (minR, minC, maxR, maxC);
            var inside = new HashSet<int>();
            for (int r = minR; r <= maxR; r++)
                for (int c = minC; c <= maxC; c++)
                    inside.Add(CellMap[r][c]);
            foreach (var zone in inside) Grow(zone);
            if (before == (minR, minC, maxR, maxC)) return before;
        }
    }

    // Moves a grid line to position (0..10000), keeping both neighbours at least MinPercent wide.
    // Vertical means a column edge; edge runs 1..Columns-1 (or 1..Rows-1).
    public void MoveEdge(bool vertical, int edge, int position)
    {
        var percents = vertical ? ColumnPercents : RowPercents;
        if (edge < 1 || edge >= percents.Length) return;
        var starts = Starts(percents);
        if (starts[edge + 1] - starts[edge - 1] < 2 * MinPercent) return;

        int pos = Math.Clamp(position, starts[edge - 1] + MinPercent, starts[edge + 1] - MinPercent);
        percents[edge - 1] = pos - starts[edge - 1];
        percents[edge] = starts[edge + 1] - pos;
    }

    // Every stretch of grid line that actually separates two zones.
    public List<Divider> Dividers()
    {
        var dividers = new List<Divider>();
        for (int e = 1; e < Columns; e++)
            AddRuns(dividers, true, e, Rows, i => CellMap[i][e - 1] != CellMap[i][e]);
        for (int e = 1; e < Rows; e++)
            AddRuns(dividers, false, e, Columns, i => CellMap[e - 1][i] != CellMap[e][i]);
        return dividers;
    }

    static void AddRuns(List<Divider> dividers, bool vertical, int edge, int count, Func<int, bool> separates)
    {
        int start = -1;
        for (int i = 0; i <= count; i++)
        {
            bool on = i < count && separates(i);
            if (on && start < 0) start = i;
            else if (!on && start >= 0)
            {
                dividers.Add(new Divider(vertical, edge, start, i - 1));
                start = -1;
            }
        }
    }

    void CopyGridFrom(GridLayout other)
    {
        RowPercents = other.RowPercents;
        ColumnPercents = other.ColumnPercents;
        CellMap = other.CellMap;
    }

    // Drops rows and columns that no longer separate anything, then numbers zones
    // left to right, top to bottom.
    void Normalize()
    {
        var cols = ColumnPercents.ToList();
        var rows = RowPercents.ToList();
        var map = CellMap.Select(r => r.ToList()).ToList();

        for (int c = cols.Count - 1; c > 0; c--)
            if (map.All(row => row[c] == row[c - 1]))
            {
                cols[c - 1] += cols[c];
                cols.RemoveAt(c);
                foreach (var row in map) row.RemoveAt(c);
            }

        for (int r = rows.Count - 1; r > 0; r--)
            if (map[r].SequenceEqual(map[r - 1]))
            {
                rows[r - 1] += rows[r];
                rows.RemoveAt(r);
                map.RemoveAt(r);
            }

        var numbers = new Dictionary<int, int>();
        foreach (var zone in map.SelectMany(r => r))
            numbers.TryAdd(zone, numbers.Count);

        ColumnPercents = [.. cols];
        RowPercents = [.. rows];
        CellMap = map.Select(r => r.Select(z => numbers[z]).ToArray()).ToArray();
    }
}
