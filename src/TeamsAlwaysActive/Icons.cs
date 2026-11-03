using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TeamsAlwaysActive;

// Rounded tile with a white coffee cup: green with steam when keeping active, grey when not.
// Geometry is on a 16-unit grid and scaled, so it stays crisp at any DPI.
// ponytail: app.ico (exe icon) is this same drawing rendered at 16–256 px; re-render it if the design changes.
static class Icons
{
    public static Icon Create(bool active, int size)
    {
        using var bmp = Draw(active, size);
        return Icon.FromHandle(bmp.GetHicon()); // ponytail: two icons for the app's lifetime, HICON never freed
    }

    static Bitmap Draw(bool active, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float u = size / 16f;

        var (top, bottom) = active
            ? (Color.FromArgb(52, 211, 153), Color.FromArgb(5, 150, 105))
            : (Color.FromArgb(156, 163, 175), Color.FromArgb(75, 85, 99));
        using (var tile = Rounded(new RectangleF(0, 0, size, size), 3.6f * u, roundTop: true))
        using (var fill = new LinearGradientBrush(new RectangleF(0, -1, size, size + 2), top, bottom, 90f))
            g.FillPath(fill, tile);

        using var white = new SolidBrush(Color.White);
        using var pen = new Pen(Color.White, 1.35f * u);
        using (var cup = Rounded(new RectangleF(3.2f * u, 6.6f * u, 7.4f * u, 6.2f * u), 2.4f * u, roundTop: false))
            g.FillPath(white, cup);
        g.DrawArc(pen, 9.2f * u, 7.6f * u, 3.4f * u, 3.4f * u, -90, 180);

        if (active)
        {
            pen.Width = 1.1f * u;
            pen.StartCap = pen.EndCap = LineCap.Round;
            foreach (float x in new[] { 5.4f, 8.2f })
                g.DrawBezier(pen, x * u, 5.2f * u, (x - 1.1f) * u, 4.2f * u, (x + 1.1f) * u, 3.2f * u, x * u, 2.0f * u);
        }
        return bmp;
    }

    // Rectangle with rounded bottom corners, and rounded top corners when roundTop.
    static GraphicsPath Rounded(RectangleF r, float radius, bool roundTop)
    {
        var p = new GraphicsPath();
        float d = radius * 2;
        if (roundTop)
        {
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        }
        else p.AddLine(r.X, r.Y, r.Right, r.Y);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
