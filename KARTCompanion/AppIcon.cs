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
    public static Icon CreateTrayIcon(Bitmap logo)
    {
        using var resized = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(logo, 0, 0, 32, 32);
        }
        return Icon.FromHandle(resized.GetHicon());
    }
}
