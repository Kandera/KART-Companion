using System.Globalization;
using System.Text;
using KARTCompanion.Matching;

namespace KARTCompanion.SavedVariables;

/// <summary>
/// Serializes a CacheModel into the Lua table literal for the "KART_WoWUtilsCache = { ... }"
/// SavedVariables block. Nested tables are indented (unlike Blizzard's own flat/unindented
/// serializer output) specifically so that SavedVariablesBlockWriter's block-boundary detection
/// — which looks for a line that is a bare "}" at column 0 — only ever matches the outermost
/// closing brace, never a nested one.
/// </summary>
public static class LuaTableWriter
{
    private const string VariableName = "KART_WoWUtilsCache";

    public static string WriteAssignment(CacheModel model)
    {
        var sb = new StringBuilder();
        sb.Append(VariableName).Append(" = ");
        WriteTable(sb, 0, w =>
        {
            WriteField(w, 1, "schemaVersion", model.SchemaVersion);
            WriteField(w, 1, "syncedAt", model.SyncedAt.ToUnixTimeSeconds());
            if (model.GroupId is not null) WriteField(w, 1, "groupId", model.GroupId);
            WriteFieldRaw(w, 1, "players", () => WritePlayers(w, 1, model.Players));
        });
        sb.Append('\n');
        return sb.ToString();
    }

    private static void WritePlayers(StringBuilder sb, int depth, Dictionary<string, List<GainCandidate>> players)
    {
        WriteTable(sb, depth, w =>
        {
            foreach (var (key, candidates) in players)
            {
                WriteFieldRaw(w, depth + 1, key, () => WriteCandidateList(w, depth + 1, candidates));
            }
        });
    }

    private static void WriteCandidateList(StringBuilder sb, int depth, List<GainCandidate> candidates)
    {
        WriteTable(sb, depth, w =>
        {
            foreach (var c in candidates)
            {
                Indent(w, depth + 1);
                WriteTable(w, depth + 1, w2 =>
                {
                    WriteField(w2, depth + 2, "itemId", c.ItemId);
                    if (c.Ilvl is not null) WriteField(w2, depth + 2, "ilvl", c.Ilvl.Value);
                    if (c.Slot is not null) WriteField(w2, depth + 2, "slot", c.Slot);
                    WriteField(w2, depth + 2, "gainPct", c.GainPct);
                    WriteField(w2, depth + 2, "source", c.Source);
                    if (c.ProfileKey is not null) WriteField(w2, depth + 2, "profileKey", c.ProfileKey);
                    WriteField(w2, depth + 2, "importedAt", c.ImportedAt.ToUnixTimeSeconds());
                });
                w.Append(",\n");
            }
        });
    }

    private static void WriteTable(StringBuilder sb, int depth, Action<StringBuilder> writeBody)
    {
        sb.Append("{\n");
        writeBody(sb);
        Indent(sb, depth);
        sb.Append('}');
    }

    private static void WriteField(StringBuilder sb, int depth, string key, object value)
    {
        Indent(sb, depth);
        sb.Append('[').Append(LuaQuote(key)).Append("] = ").Append(LuaValue(value)).Append(",\n");
    }

    private static void WriteFieldRaw(StringBuilder sb, int depth, string key, Action writeValue)
    {
        Indent(sb, depth);
        sb.Append('[').Append(LuaQuote(key)).Append("] = ");
        writeValue();
        sb.Append(",\n");
    }

    private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 4);

    private static string LuaValue(object value) => value switch
    {
        string s => LuaQuote(s),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("0.####", CultureInfo.InvariantCulture),
        _ => LuaQuote(value.ToString() ?? ""),
    };

    private static string LuaQuote(string s)
    {
        var escaped = s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
        return "\"" + escaped + "\"";
    }
}
