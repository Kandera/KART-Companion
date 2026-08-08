using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KARTCompanion.SavedVariables;

namespace KARTCompanion.Export;

/// <summary>
/// Emits the same RCLootCouncil-compatible JSON the addon's own LH.BuildRCLootCouncilJSON produces,
/// field for field, so the maintainer can paste either one into a tool built to read that format.
///
/// <c>itemID</c>, <c>subType</c> and <c>equipLoc</c> all come, on the addon's side, from a single
/// call to C_Item.GetItemInfoInstant — the running game client's item database. The Companion has no
/// such database and no equivalent API, so all three are always emitted empty/zero here, never parsed
/// out of the item link even where that would be possible for the id alone. That is not a shortcut:
/// it is what keeps this writer matching the addon's own behavior for any item that client has not
/// (yet) cached, which is the same "unresolved" case the addon itself falls back to a zero id for.
///
/// <c>Write</c> always sorts newest-first and ignores the order it was given awards in. Every real
/// caller wants exactly this — it is what LH.BuildRCLootCouncilJSON's own default (no explicit list)
/// produces, and the export dialog has no other order to offer — so the writer owns it rather than
/// pushing the same sort onto every call site.
///
/// The date/time fields are rendered in a caller-supplied time zone, defaulting to the machine's
/// local zone (matching the addon, which uses Lua's date() and is therefore always local to whatever
/// machine WoW is running on). The override exists so a test can pin the zone the golden fixture was
/// actually captured in, rather than depending on whichever machine happens to run the test.
/// </summary>
public static class RcLootCouncilJsonWriter
{
    // Canonical English difficulty names, mirroring LootHistory.lua's DIFFICULTY_EN. Keyed by
    // Blizzard's difficultyID; entries logged before that field existed fall back to the stored
    // (possibly localized) difficulty string, exactly as the addon's LH.DifficultyExport does.
    private static readonly Dictionary<long, string> DifficultyEn = new()
    {
        [1] = "Normal",
        [2] = "Heroic",
        [3] = "10 Player",
        [4] = "25 Player",
        [5] = "10 Player (Heroic)",
        [6] = "25 Player (Heroic)",
        [7] = "LFR",
        [8] = "Mythic Keystone",
        [9] = "40 Player",
        [14] = "Normal",
        [15] = "Heroic",
        [16] = "Mythic",
        [17] = "LFR",
        [23] = "Mythic",
        [24] = "Timewalking",
        [33] = "Timewalking",
        [208] = "Delve",
    };

    // |H(item:...)|h — the item string exactly as the client wrote it, matched by delimiter so no
    // assumption is made about which separators a given client build uses inside it.
    private static readonly Regex ItemStringPattern = new(@"\|H(item:[^|]+)\|h", RegexOptions.Compiled);

    // The display name inside a hyperlink's brackets: |c...|Hitem:...|h[Name]|h|r.
    private static readonly Regex ItemNamePattern = new(@"\[(.*?)\]", RegexOptions.Compiled);

    public static string Write(IEnumerable<LootHistoryEntry> awards, TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;

        // Newest first, mirroring LH.GetFilteredEntries. Awards sharing a second keep the order the
        // saved-variables file had; the addon leaves that order to Lua's table.sort, which does not
        // specify it, so the Companion pins it deliberately instead of imitating it.
        var list = awards.OrderByDescending(e => e.Time).ToList();

        var objects = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            objects.Add(WriteObject(list[i], i + 1, zone));
        }
        return "[" + string.Join(",", objects) + "]";
    }

    private static string WriteObject(LootHistoryEntry e, int index, TimeZoneInfo timeZone)
    {
        var time = e.Time;
        var localTime = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(time), timeZone);

        var fields = new[]
        {
            JsonString("player", e.Winner ?? ""),
            JsonString("date", localTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)),
            JsonString("time", localTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            JsonString("id", $"{time}-{index}"),
            JsonNumber("itemID", 0),
            JsonString("itemString", GetItemString(e.Item)),
            JsonString("response", e.Reason ?? ""),
            JsonNumber("votes", 0),
            JsonString("class", e.Class ?? ""),
            JsonString("instance", DifficultyExport(e)),
            JsonString("boss", ""),
            JsonString("gear1", ""),
            JsonString("gear2", ""),
            JsonString("responseID", "0"),
            JsonString("isAwardReason", "false"),
            JsonString("rollType", "normal"),
            // No item database to resolve these from — see the class remarks.
            JsonString("subType", ""),
            JsonString("equipLoc", ""),
            JsonString("note", ""),
            JsonString("owner", ""),
            JsonString("itemName", GetItemNameFromLink(e.Item)),
            JsonString("servertime", time.ToString(CultureInfo.InvariantCulture)),
        };
        return "{" + string.Join(",", fields) + "}";
    }

    private static string DifficultyExport(LootHistoryEntry e) =>
        (e.DifficultyId is { } id && DifficultyEn.TryGetValue(id, out var name)) ? name : (e.Difficulty ?? "");

    private static string GetItemString(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemStringPattern.Match(link);
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string GetItemNameFromLink(string? link)
    {
        if (string.IsNullOrEmpty(link)) return "";
        var m = ItemNamePattern.Match(link);
        return m.Success ? m.Groups[1].Value : link;
    }

    private static string JsonString(string key, string value) =>
        $"\"{key}\":\"{JsonEscape(value)}\"";

    private static string JsonNumber(string key, long value) =>
        $"\"{key}\":{value.ToString(CultureInfo.InvariantCulture)}";

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c <= '\u001f') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
