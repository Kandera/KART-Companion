namespace KARTCompanion;

/// <summary>Loads the KART logo (embedded from Assets/KAimg.jpg, the same image the WoW addon
/// itself uses as its icon texture) for the tray icon and dialog branding.</summary>
public static class AppIcon
{
    private const string ResourceName = "KARTCompanion.Assets.KAimg.jpg";

    public static Bitmap LoadLogoBitmap()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        return new Bitmap(stream);
    }

    /// <summary>Downscales the (large, square) source logo to a crisp 32x32 icon. The returned
    /// Icon's native handle lives for the process lifetime — fine for a single tray icon on a
    /// long-running app, not worth the extra DestroyIcon plumbing for one handle.</summary>
    public static Icon CreateTrayIcon(Bitmap logo) => CreateTrayIcon(logo, statusDotColor: null);

    /// <summary>Same as <see cref="CreateTrayIcon(Bitmap)"/>, but composites a small solid-color
    /// dot onto the bottom-right corner when <paramref name="statusDotColor"/> is not null — used
    /// so sync state (syncing/error) is visible at a glance without hovering the tray tooltip.</summary>
    public static Icon CreateTrayIcon(Bitmap logo, Color? statusDotColor)
    {
        const int dotDiameter = 12;

        using var resized = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, 0, 0, 32, 32);

            if (statusDotColor is { } color)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var dotRect = new Rectangle(32 - dotDiameter, 32 - dotDiameter, dotDiameter, dotDiameter);
                using var brush = new SolidBrush(color);
                g.FillEllipse(brush, dotRect);
            }
        }
        return Icon.FromHandle(resized.GetHicon());
    }
}
