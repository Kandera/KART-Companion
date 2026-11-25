using KARTCompanion.Archive;
using KARTCompanion.Export;

namespace KARTCompanion.Tests;

public class WowUtilsUploadPlannerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void Select_OmitsWithdrawnExcludedAndEmptyId()
    {
        var awards = new[]
        {
            Award("keep"),
            Award("gone", withdrawn: true),
            Award("skip", excluded: true),
            Award(""),
        };

        var selected = WowUtilsUploadPlanner.SelectForPreview(awards);

        Assert.Equal(new[] { "keep" }, selected.Select(a => a.Fields["id"]).ToArray());
        Assert.Equal(1, WowUtilsUploadPlanner.CountMissingId(awards));
    }

    [Fact]
    public void Select_OmitsBonusRollsAndDoesNotCountThemAsMissingId()
    {
        var numeric = Award("numeric");
        numeric.Fields["responseID"] = 3d;
        var numericMissingId = Award("");
        numericMissingId.Fields["responseID"] = 3d;

        var awards = new[]
        {
            Award("keep", responseId: "1"),
            Award("roll", responseId: "BONUSROLL"),
            Award("roll-alt", responseId: "BONUS_ROLL"),
            Award("", responseId: "BONUSROLL"),
            Award("", responseId: "BONUS_ROLL"),
            numeric,
            numericMissingId,
        };

        var selected = WowUtilsUploadPlanner.SelectForPreview(awards);

        Assert.Equal(new[] { "keep", "numeric" }, selected.Select(a => a.Fields["id"]).ToArray());
        Assert.Equal(1, WowUtilsUploadPlanner.CountMissingId(awards));
    }

    [Fact]
    public void Decide_AllDuplicate_WritesReceiptsAndDoesNotConfirm()
    {
        var awards = new[] { Award("a"), Award("b") };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("a", PreviewClass.Duplicate), ("b", PreviewClass.Duplicate)), 0);

        Assert.False(decision.Stopped);
        Assert.Equal(new[] { "a", "b" }, decision.ReceiptIds);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_AllNew_ConfirmsThoseIds()
    {
        var awards = new[] { Award("a") };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("a", PreviewClass.New)), 2);

        Assert.False(decision.Stopped);
        Assert.Empty(decision.ReceiptIds);
        Assert.Equal(new[] { "a" }, decision.ConfirmIds);
        Assert.Contains("2", decision.Message);
    }

    [Fact]
    public void Decide_ReceiptedIdClassifiedNew_StopsWithNoReceipts()
    {
        var awards = new[] { Award("old", imported: Now), Award("fresh") };
        var decision = WowUtilsUploadPlanner.Decide(
            awards,
            Preview(("old", PreviewClass.New), ("fresh", PreviewClass.New)),
            0);

        Assert.True(decision.Stopped);
        Assert.Empty(decision.ReceiptIds);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_UncertainIdClassifiedNew_Stops()
    {
        var awards = new[] { Award("u", uncertain: Now) };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("u", PreviewClass.New)), 0);

        Assert.True(decision.Stopped);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_UncertainIdClassifiedDuplicate_ReceiptsAndClearsTheConfirmSet()
    {
        var awards = new[] { Award("u", uncertain: Now) };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("u", PreviewClass.Duplicate)), 0);

        Assert.False(decision.Stopped);
        Assert.Equal(new[] { "u" }, decision.ReceiptIds);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_MissingIdInPreview_Stops()
    {
        var awards = new[] { Award("a"), Award("b") };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("a", PreviewClass.New)), 0);

        Assert.True(decision.Stopped);
        Assert.Empty(decision.ReceiptIds);
    }

    [Fact]
    public void Decide_CountOnlyPreview_Stops()
    {
        var awards = new[] { Award("a") };
        var decision = WowUtilsUploadPlanner.Decide(awards, new UploadPreview(false, new Dictionary<string, PreviewClass>()), 0);

        Assert.True(decision.Stopped);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_InvalidAndTooOld_AreNotReceiptedOrConfirmed()
    {
        var awards = new[] { Award("bad"), Award("old") };
        var decision = WowUtilsUploadPlanner.Decide(
            awards,
            Preview(("bad", PreviewClass.Invalid), ("old", PreviewClass.TooOld)),
            0);

        Assert.False(decision.Stopped);
        Assert.Empty(decision.ReceiptIds);
        Assert.Empty(decision.ConfirmIds);
    }

    [Fact]
    public void Decide_ClipboardStampDoesNotExcludeOrCountAsImported()
    {
        var awards = new[] { Award("a", clipboard: Now) };
        var decision = WowUtilsUploadPlanner.Decide(awards, Preview(("a", PreviewClass.New)), 0);

        Assert.False(decision.Stopped);
        Assert.Equal(new[] { "a" }, decision.ConfirmIds);
    }

    [Fact]
    public void ApplyReceipts_SetsImportedAndClearsUncertain()
    {
        var award = Award("a", uncertain: Now);
        WowUtilsUploadPlanner.ApplyReceipts(new[] { award }, new[] { "a" }, Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), award.WowUtilsImportedAt);
        Assert.Null(award.WowUtilsConfirmUncertainAt);
        Assert.Null(award.ExportedByCompanionAt);
    }

    [Fact]
    public void MarkUncertain_DoesNotSetAReceipt()
    {
        var award = Award("a");
        WowUtilsUploadPlanner.MarkUncertain(new[] { award }, new[] { "a" }, Now);

        Assert.Equal(Now, award.WowUtilsConfirmUncertainAt);
        Assert.Null(award.WowUtilsImportedAt);
    }

    private static UploadPreview Preview(params (string Id, PreviewClass Class)[] rows) =>
        new(true, rows.ToDictionary(r => r.Id, r => r.Class));

    private static ArchivedAward Award(
        string id,
        bool withdrawn = false,
        bool excluded = false,
        DateTimeOffset? imported = null,
        DateTimeOffset? uncertain = null,
        DateTimeOffset? clipboard = null,
        string? responseId = null) =>
        new()
        {
            Fields = new Dictionary<string, object?> { ["id"] = id, ["responseID"] = responseId },
            Withdrawn = withdrawn,
            ExcludedFromExport = excluded,
            WowUtilsImportedAt = imported,
            WowUtilsConfirmUncertainAt = uncertain,
            ExportedByCompanionAt = clipboard,
        };
}
