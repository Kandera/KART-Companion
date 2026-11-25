using KARTCompanion.Archive;

namespace KARTCompanion.Export;

/// <summary>
/// Which loot reasons an upload sends. The key is the award's effective reason text, the same
/// string the history list shows in the Reason column. An award with no reason uses the empty
/// string, shown as <see cref="NoReasonLabel"/>.
/// </summary>
public static class UploadReasons
{
    public const string NoReasonLabel = "No reason";

    public static string Key(ArchivedAward award) =>
        award.EffectiveFields.TryGetValue("reason", out var v) && v is string s && s.Length > 0 ? s : "";

    public static string Label(string key) => key.Length == 0 ? NoReasonLabel : key;

    public static bool IsLeftOut(ArchivedAward award, IReadOnlySet<string>? excluded) =>
        excluded != null && excluded.Count > 0 && excluded.Contains(Key(award));

    /// <summary>A sentence for the history footer. Empty when every reason is included.</summary>
    public static string Summary(IReadOnlySet<string> excluded)
    {
        if (excluded.Count == 0) return "";
        var names = excluded
            .Select(Label)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (names.Count <= 3)
            return " Upload leaves out " + string.Join(", ", names) + ".";
        return $" Upload leaves out {names.Count} reasons.";
    }
}
