using KARTCompanion.Shell;

namespace KARTCompanion.Tests;

public class SettingsScreenTests
{
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

    // The row sits a fixed margin above the bottom edge, and the button's OWN height decides where
    // its top goes. The previous version hard-coded a row height of 30 against buttons Theme.cs
    // builds 34 tall, so the real margin was 36 and nothing could see it — the layout arithmetic is
    // otherwise entirely untested, because nothing in this suite constructs a Form.
    [Fact]
    public void ActionRowTop_LeavesTheMarginBelowTheButtonsOwnHeight()
    {
        var top = SettingsScreenText.ActionRowTop(viewHeight: 700, buttonHeight: 34);

        Assert.Equal(626, top);
        Assert.Equal(SettingsScreenText.ActionRowBottomMargin, 700 - (top + 34));
    }

    // A taller button must move the row UP, not push it through the bottom edge.
    [Fact]
    public void ActionRowTop_TallerButtonKeepsTheSameBottomMargin()
    {
        var top = SettingsScreenText.ActionRowTop(viewHeight: 700, buttonHeight: 50);

        Assert.Equal(SettingsScreenText.ActionRowBottomMargin, 700 - (top + 50));
    }
}
