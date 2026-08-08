using System.Drawing;
using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

public class SettingsScreenTests
{
    // Guards the one number CompanionShell now hands out as ClientSize for every screen — see
    // CompanionShell's own remarks. HistoryScreen needs the width for its six columns
    // (ContentLeft 80 + 950 + 12); a regression here silently drifts the whole shell's frame away
    // from what the history list actually needs, the same drift this task exists to remove.
    [Fact]
    public void ScreenSize_MatchesTheHistoryScreenContentWidth()
    {
        Assert.Equal(new Size(1042, 700), CompanionShell.ScreenSize);
    }

    [Fact]
    public void BuildWatchingText_NoPathConfigured_SaysSoInsteadOfNaming()
    {
        Assert.Equal(
            "No SavedVariables file configured yet — pick your WoW install folder below.",
            SettingsScreenText.BuildWatchingText(null));
    }

    [Fact]
    public void BuildWatchingText_BlankPath_TreatedTheSameAsNoPath()
    {
        Assert.Equal(
            "No SavedVariables file configured yet — pick your WoW install folder below.",
            SettingsScreenText.BuildWatchingText("   "));
    }

    [Fact]
    public void BuildWatchingText_PathConfigured_NamesIt()
    {
        Assert.Equal(
            @"Watching C:\WoW\_retail_\WTF\Account\ACC\SavedVariables\KeineAhnungRaidTools.lua",
            SettingsScreenText.BuildWatchingText(@"C:\WoW\_retail_\WTF\Account\ACC\SavedVariables\KeineAhnungRaidTools.lua"));
    }

    [Fact]
    public void BuildLastSyncText_NeverSynced_IsEmpty()
    {
        Assert.Equal("", SettingsScreenText.BuildLastSyncText(null));
    }

    // A fixed zone rather than TimeZoneInfo.Local, for the same reason
    // HistoryExportPlanner.ParseFromDate takes one: a test whose expected value comes from the same
    // local-time conversion under test can never fail, whatever zone the machine happens to be in.
    [Fact]
    public void BuildLastSyncText_Synced_FormatsAsLocalHourAndMinute()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
        var syncedAt = new DateTimeOffset(2026, 8, 8, 20, 41, 0, TimeSpan.Zero);

        Assert.Equal("Last sync 22:41", SettingsScreenText.BuildLastSyncText(syncedAt, zone));
    }
}
