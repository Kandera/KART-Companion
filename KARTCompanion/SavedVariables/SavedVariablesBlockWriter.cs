namespace KARTCompanion.SavedVariables;

/// <summary>
/// Safely updates ONLY the "KART_WoWUtilsCache = { ... }" block inside the addon's
/// SavedVariables Lua file, leaving every other declared variable (KART_Settings,
/// KART_LootHistory, KART_LCOfficerNotes) byte-for-byte untouched. This is the highest-risk
/// piece of the whole companion — a bug here could corrupt the player's real settings/loot
/// history — so the block-isolation logic is kept as simple and conservative as possible:
///
/// WoW's SavedVariables serializer always writes each top-level declared variable as its own
/// flush-left "VarName = {" ... "}" span, and — critically — nested table VALUES always close
/// with "}," (a trailing comma, since they're an entry inside a larger table) while the
/// outermost assignment's own closing brace stands alone as just "}" with nothing else on the
/// line. That makes a bare "}" line an unambiguous marker for "end of this top-level
/// assignment", regardless of how deeply nested the tables inside it are.
/// </summary>
public static class SavedVariablesBlockWriter
{
    private const string VariableName = "KART_WoWUtilsCache";
    private static readonly string AssignmentHeader = VariableName + " = {";

    /// <summary>Reads the file, replaces (or appends) the KART_WoWUtilsCache block, and writes
    /// the result back via a temp-file + atomic rename so a crash mid-write can't leave the
    /// file half-written.</summary>
    public static void WriteBlock(string filePath, string blockText)
    {
        var original = File.Exists(filePath) ? File.ReadAllText(filePath) : "";
        var updated = ReplaceOrAppendBlock(original, blockText);

        var tempPath = filePath + ".kartcompanion.tmp";
        File.WriteAllText(tempPath, updated);

        if (File.Exists(filePath))
            File.Replace(tempPath, filePath, null);
        else
            File.Move(tempPath, filePath);
    }

    /// <summary>Pure function (no file I/O) so the splicing logic can be unit-tested without a
    /// real file on disk.</summary>
    public static string ReplaceOrAppendBlock(string originalContent, string blockText)
    {
        var lines = originalContent.Replace("\r\n", "\n").Split('\n').ToList();

        var startIndex = lines.FindIndex(l => l == AssignmentHeader);
        if (startIndex < 0)
        {
            // Variable not declared/present yet — append. Ensure exactly one blank-line
            // separation from whatever precedes it.
            var trimmed = originalContent.TrimEnd('\n', '\r');
            var separator = trimmed.Length == 0 ? "" : "\n";
            return trimmed + separator + blockText.TrimEnd('\n') + "\n";
        }

        var endIndex = -1;
        for (var i = startIndex + 1; i < lines.Count; i++)
        {
            if (lines[i] == "}")
            {
                endIndex = i;
                break;
            }
        }

        if (endIndex < 0)
        {
            // Malformed/truncated existing block (no matching close found) — refuse to guess;
            // append a fresh block after the whole file rather than risk mangling something we
            // don't understand. The stray old block is left in place for a human to notice.
            var trimmed = originalContent.TrimEnd('\n', '\r');
            return trimmed + "\n" + blockText.TrimEnd('\n') + "\n";
        }

        var before = lines.Take(startIndex);
        var after = lines.Skip(endIndex + 1);
        var newLines = before.Concat(blockText.TrimEnd('\n').Split('\n')).Concat(after);
        return string.Join("\n", newLines).TrimEnd('\n') + "\n";
    }
}
