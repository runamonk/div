using System.Text.Json.Serialization;

namespace WindowWrangler.Core;

// Screen rectangle in physical pixels. Right and Bottom are exclusive, same as a Win32 RECT.
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    [JsonIgnore] public int Width => Right - Left;
    [JsonIgnore] public int Height => Bottom - Top;
    [JsonIgnore] public long Area => Width <= 0 || Height <= 0 ? 0 : (long)Width * Height;

    public Rect Intersect(Rect other)
    {
        var r = new Rect(Math.Max(Left, other.Left), Math.Max(Top, other.Top),
                         Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
        return r.Area == 0 ? default : r;
    }

    public Rect Union(Rect other) =>
        new(Math.Min(Left, other.Left), Math.Min(Top, other.Top),
            Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    // Intersection over union: 1 means identical, 0 means no overlap.
    public double OverlapRatio(Rect other)
    {
        long inter = Intersect(other).Area;
        long union = Area + other.Area - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    // How far the furthest-off edge is from the matching edge of other.
    public int EdgeDistance(Rect other) =>
        Math.Max(Math.Max(Math.Abs(Left - other.Left), Math.Abs(Top - other.Top)),
                 Math.Max(Math.Abs(Right - other.Right), Math.Abs(Bottom - other.Bottom)));

    public bool NearlyEquals(Rect other, int tolerance) =>
        Math.Abs(Left - other.Left) <= tolerance && Math.Abs(Top - other.Top) <= tolerance &&
        Math.Abs(Right - other.Right) <= tolerance && Math.Abs(Bottom - other.Bottom) <= tolerance;

    public override string ToString() => $"({Left},{Top} {Width}x{Height})";
}
