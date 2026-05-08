using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WindowsRightClickLock.UI;

/// <summary>
/// Dev-only helper. Run the binary with `--preview-icons [outDir]` to dump 32x32 native
/// renderings plus a 4x nearest-neighbor upscale of each tray icon for visual review.
/// </summary>
internal static class PreviewIcons
{
    public static void WriteToDisk(string outDir)
    {
        Directory.CreateDirectory(outDir);
        Save(TrayIcons.CreateIdle(), Path.Combine(outDir, "tray-idle"));
        Save(TrayIcons.CreateLocked(), Path.Combine(outDir, "tray-locked"));
        Console.WriteLine($"Wrote icon previews to: {Path.GetFullPath(outDir)}");
    }

    private static void Save(Icon icon, string baseName)
    {
        using (icon)
        using (var bmp = icon.ToBitmap())
        {
            bmp.Save(baseName + "-32.png", ImageFormat.Png);
            using var up = new Bitmap(128, 128);
            using (var g = Graphics.FromImage(up))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, 0, 0, 128, 128);
            }
            up.Save(baseName + "-128.png", ImageFormat.Png);
        }
    }
}
