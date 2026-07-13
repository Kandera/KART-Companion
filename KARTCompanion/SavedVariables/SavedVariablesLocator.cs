namespace KARTCompanion.SavedVariables;

/// <summary>
/// Finds KeineAhnungRaidTools.lua under a WoW retail install's WTF/Account folder. KART has no
/// SavedVariablesPerCharacter, so this is account-wide — one file per Battle.net account folder,
/// not per character.
/// </summary>
public static class SavedVariablesLocator
{
    public static IReadOnlyList<string> FindSavedVariablesFiles(string wowInstallRoot)
    {
        return FindAccountDir(wowInstallRoot) is { } accountDir
            ? Directory.GetDirectories(accountDir)
                .Select(acc => Path.Combine(acc, "SavedVariables", "KeineAhnungRaidTools.lua"))
                .Where(File.Exists)
                .ToList()
            : Array.Empty<string>();
    }

    // Accepts either the WoW install root (the folder that CONTAINS "_retail_") or the
    // "_retail_" folder itself — a folder picker inherently invites both interpretations of
    // "select the folder _retail_ is in", so this is tolerant of either.
    private static string? FindAccountDir(string selectedPath)
    {
        var direct = Path.Combine(selectedPath, "_retail_", "WTF", "Account");
        if (Directory.Exists(direct)) return direct;

        var asRetailItself = Path.Combine(selectedPath, "WTF", "Account");
        if (Path.GetFileName(selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Equals("_retail_", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(asRetailItself))
        {
            return asRetailItself;
        }

        return null;
    }

    /// <summary>Best-effort scan of common install locations, used on first run before the user
    /// has configured a WoW install path explicitly.</summary>
    public static IReadOnlyList<string> ScanCommonInstallPaths()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            roots.Add(Path.Combine(drive.Name, "World of Warcraft"));
            roots.Add(Path.Combine(drive.Name, "Program Files (x86)", "World of Warcraft"));
            roots.Add(Path.Combine(drive.Name, "Games", "World of Warcraft"));
        }

        var found = new List<string>();
        foreach (var root in roots.Distinct())
        {
            if (Directory.Exists(Path.Combine(root, "_retail_")))
                found.AddRange(FindSavedVariablesFiles(root));
        }

        return found.Distinct().ToList();
    }
}
