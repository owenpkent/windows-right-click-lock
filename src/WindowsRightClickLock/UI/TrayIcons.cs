using System.Drawing.Drawing2D;
using WindowsRightClickLock.Native;

namespace WindowsRightClickLock.UI;

/// <summary>
/// Renders tray icons at runtime via GDI+. Both icons show a stylised mouse silhouette;
/// the right button area is tinted differently to convey state at a glance:
///   - Idle:   muted steel blue
///   - Locked: vivid red
/// Cloned icons own their own HICONs; the originals from <see cref="Bitmap.GetHicon"/>
/// are destroyed here so we don't leak GDI handles.
/// </summary>
internal static class TrayIcons
{
    public static Icon CreateIdle() => CreateMouseIcon(Color.FromArgb(80, 130, 185));
    public static Icon CreateLocked() => CreateMouseIcon(Color.FromArgb(220, 40, 40));

    private static Icon CreateMouseIcon(Color rightButtonColor)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Body bounds chosen so the mouse fills the icon vertically with a small margin.
        var bodyRect = new RectangleF(7f, 3.5f, 18f, 25.5f);
        using var body = RoundedRect(bodyRect, 8f);

        // 1. Body fill: subtle vertical gradient.
        using (var bodyBrush = new LinearGradientBrush(
                   bodyRect,
                   Color.FromArgb(245, 245, 250),
                   Color.FromArgb(195, 195, 205),
                   LinearGradientMode.Vertical))
        {
            g.FillPath(bodyBrush, body);
        }

        // 2. Right button tint: drawn clipped to the body so it conforms to the silhouette.
        var saved = g.Save();
        g.SetClip(body);
        var rightHalf = new RectangleF(16f, 3.5f, 9f, 11.5f);
        using (var rb = new SolidBrush(Color.FromArgb(225, rightButtonColor)))
        {
            g.FillRectangle(rb, rightHalf);
        }
        g.Restore(saved);

        // 3. Button seams: horizontal across body, vertical above the seam.
        using (var seam = new Pen(Color.FromArgb(140, 145, 155), 1f))
        {
            g.DrawLine(seam, 8f, 14.5f, 24f, 14.5f);
            g.DrawLine(seam, 16f, 4f, 16f, 14.5f);
        }

        // 4. Scroll wheel: a small dark pill at the top center.
        var wheelRect = new RectangleF(14.75f, 7f, 2.5f, 5.5f);
        using (var wheelPath = RoundedRect(wheelRect, 1f))
        {
            using var wheelBrush = new SolidBrush(Color.FromArgb(95, 95, 110));
            g.FillPath(wheelBrush, wheelPath);
            using var wheelPen = new Pen(Color.FromArgb(60, 60, 75), 0.7f);
            g.DrawPath(wheelPen, wheelPath);
        }

        // 5. Body outline: drawn last so it sits cleanly on top of all fills.
        using (var outline = new Pen(Color.FromArgb(75, 75, 90), 1.4f))
        {
            g.DrawPath(outline, body);
        }

        return BitmapToIcon(bmp);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Icon BitmapToIcon(Bitmap bmp)
    {
        var hIcon = bmp.GetHicon();
        try
        {
            using var fromHandle = Icon.FromHandle(hIcon);
            return (Icon)fromHandle.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
