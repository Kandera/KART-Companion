using Microsoft.Win32;

namespace KARTCompanion;

/// <summary>Manages the per-user "start with Windows" registry entry
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). The registry itself is the source of
/// truth — deliberately not mirrored into config.json, so the Settings toggle can never drift
/// out of sync with what Windows will actually launch at logon.</summary>
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KARTCompanion";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
            // Re-written on every enable so a moved/renamed exe heals itself the next time the
            // user toggles the switch. Quoted because the install path may contain spaces.
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
