using System.Drawing.Drawing2D;
using WindowsMouseMods.Native;

namespace WindowsMouseMods.UI;

/// <summary>
/// Renders tray icons at runtime via GDI+ so we don't need to ship .ico files.
/// Each icon is a colored circle with a centered "R" — idle vs. locked is conveyed by color.
/// Cloned icons own their own HICONs; the originals from Bitmap.GetHicon are destroyed
/// here so we don't leak GDI handles.
/// </summary>
internal static class TrayIcons
{
    public static Icon CreateIdle() => CreateCircleIcon(Color.FromArgb(60, 90, 120), Color.White, "R");
    public static Icon CreateLocked() => CreateCircleIcon(Color.FromArgb(200, 35, 35), Color.White, "R");

    private static Icon CreateCircleIcon(Color fill, Color foreground, string text)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var brush = new SolidBrush(fill))
                g.FillEllipse(brush, 1, 1, size - 2, size - 2);
            using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1.2f))
                g.DrawEllipse(pen, 1, 1, size - 2, size - 2);
            using var font = new Font(FontFamily.GenericSansSerif, 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var fg = new SolidBrush(foreground);
            g.DrawString(text, font, fg, new RectangleF(0, 0, size, size), sf);
        }

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
