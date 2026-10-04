using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PixelArtConverter;

public sealed record Settings(int Columns, int Rows, int Width, int Height,
    int AlphaThreshold, int ColorStep, int BackgroundMode, Color Background, int Tolerance, int MarginLeft = 0, int MarginTop = 0,
    int MarginRight = 0, int MarginBottom = 0, bool FitEach = false, int Padding = 1,
    string ColumnWidths = "", string RowHeights = "", bool UseCentroid = false);

public static class Converter
{
    // Each destination pixel integrates the exact overlapping source area.
    // RGB is alpha-weighted: invisible RGB never darkens the outline.
    public static List<Bitmap> Convert(Bitmap source, Settings s)
    {
        var cells = SpriteLayout.GetCells(source.Width, source.Height, s);
        using var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(normalized))
        {
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImageUnscaled(source, 0, 0);
        }
        var data = normalized.LockBits(new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[source.Width * source.Height * 4];
        try
        {
            for (int y = 0; y < source.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * source.Width * 4, source.Width * 4);
        }
        finally { normalized.UnlockBits(data); }
        Color key = s.BackgroundMode == 1 ? source.GetPixel(0, 0) : s.Background;
        if (s.BackgroundMode != 0 && key.A != 0)
            for (int i = 0; i < bytes.Length; i += 4)
                if (Math.Max(Math.Abs(bytes[i] - key.B), Math.Max(Math.Abs(bytes[i + 1] - key.G),
                    Math.Abs(bytes[i + 2] - key.R))) <= s.Tolerance) bytes[i + 3] = 0;

        var bounds = new List<Rectangle>();
        var centers = new List<(double X, double Y)>();
        var extents = new List<(double W, double H)>();
        foreach (var cell in cells)
        {
            int x0 = cell.Left, x1 = cell.Right, y0 = cell.Top, y1 = cell.Bottom;
            int left = x1, top = y1, right = x0 - 1, bottom = y0 - 1;
            double mass = 0, momentX = 0, momentY = 0;
            for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
                if (bytes[(y * source.Width + x) * 4 + 3] >= Math.Max(1, s.AlphaThreshold))
                {
                    left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    double weight = bytes[(y * source.Width + x) * 4 + 3];
                    mass += weight; momentX += (x + 0.5) * weight; momentY += (y + 0.5) * weight;
                }
            var b = right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            bounds.Add(b);
            double cx = s.UseCentroid && mass > 0 ? momentX / mass : (b.Left + b.Right) / 2.0;
            double cy = s.UseCentroid && mass > 0 ? momentY / mass : (b.Top + b.Bottom) / 2.0;
            centers.Add((cx, cy));
            // Fit both sides of the chosen center, so asymmetric sprites are not clipped.
            extents.Add(b.IsEmpty ? (0.0, 0.0) : (2 * Math.Max(cx - b.Left, b.Right - cx), 2 * Math.Max(cy - b.Top, b.Bottom - cy)));
        }
        double maxW = Math.Max(1, extents.Max(e => e.W)), maxH = Math.Max(1, extents.Max(e => e.H));
        // One shared scale preserves relative sprite sizes; center each sprite in its cell.
        double sharedScale = Math.Min((double)Math.Max(1, s.Width - s.Padding * 2) / maxW, (double)Math.Max(1, s.Height - s.Padding * 2) / maxH);
        var output = new List<Bitmap>();
        try
        {
            for (int n = 0; n < cells.Count; n++)
            {
                var dest = new Bitmap(s.Width, s.Height, PixelFormat.Format32bppArgb);
                output.Add(dest);
                var b = bounds[n]; if (b.IsEmpty) continue;
                double scale = s.FitEach
                    ? Math.Min((double)(s.Width - s.Padding * 2) / extents[n].W, (double)(s.Height - s.Padding * 2) / extents[n].H)
                    : sharedScale;
                double cx = centers[n].X, cy = centers[n].Y;
                var cell = cells[n];
                for (int dy = 0; dy < s.Height; dy++)
                for (int dx = 0; dx < s.Width; dx++)
                {
                    double xa = cx + (dx - s.Width / 2.0) / scale, xb = xa + 1 / scale;
                    double ya = cy + (dy - s.Height / 2.0) / scale, yb = ya + 1 / scale;
                    double alpha = 0, red = 0, green = 0, blue = 0;
                    for (int y = Math.Max(cell.Top, (int)Math.Floor(ya)); y < Math.Min(cell.Bottom, (int)Math.Ceiling(yb)); y++)
                    for (int x = Math.Max(cell.Left, (int)Math.Floor(xa)); x < Math.Min(cell.Right, (int)Math.Ceiling(xb)); x++)
                    {
                        double area = Math.Max(0, Math.Min(xb, x + 1) - Math.Max(xa, x)) *
                                      Math.Max(0, Math.Min(yb, y + 1) - Math.Max(ya, y));
                        int i = (y * source.Width + x) * 4;
                        double weight = area * bytes[i + 3];
                        alpha += weight; blue += weight * bytes[i]; green += weight * bytes[i + 1]; red += weight * bytes[i + 2];
                    }
                    if (alpha <= 0 || alpha / ((xb - xa) * (yb - ya)) < s.AlphaThreshold) continue;
                    int Q(double value) => Math.Clamp((int)Math.Round(value / s.ColorStep) * s.ColorStep, 0, 255);
                    dest.SetPixel(dx, dy, Color.FromArgb(255, Q(red / alpha), Q(green / alpha), Q(blue / alpha)));
                }
            }
            return output;
        }
        catch { foreach (var image in output) image.Dispose(); throw; }
    }
}
