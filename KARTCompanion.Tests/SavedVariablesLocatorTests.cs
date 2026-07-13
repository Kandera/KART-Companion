using KARTCompanion.SavedVariables;

namespace KARTCompanion.Tests;

public class SavedVariablesLocatorTests : IDisposable
{
    private readonly string _tempRoot;

    public SavedVariablesLocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "kart-companion-test-" + Guid.NewGuid());
        var accountDir = Path.Combine(_tempRoot, "_retail_", "WTF", "Account", "TESTACCOUNT", "SavedVariables");
        Directory.CreateDirectory(accountDir);
        File.WriteAllText(Path.Combine(accountDir, "KeineAhnungRaidTools.lua"), "KART_Settings = {}\n");
    }

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void FindSavedVariablesFiles_GivenWowInstallRoot_FindsFile()
    {
        var matches = SavedVariablesLocator.FindSavedVariablesFiles(_tempRoot);

        Assert.Single(matches);
        Assert.EndsWith("KeineAhnungRaidTools.lua", matches[0]);
    }

    [Fact]
    public void FindSavedVariablesFiles_GivenRetailFolderItself_AlsoFindsFile()
    {
        // A folder picker inherently invites picking either the WoW root or "_retail_" itself
        // when asked to "select the folder _retail_ is in" — both must resolve the same way.
        var retailFolder = Path.Combine(_tempRoot, "_retail_");

        var matches = SavedVariablesLocator.FindSavedVariablesFiles(retailFolder);

        Assert.Single(matches);
        Assert.EndsWith("KeineAhnungRaidTools.lua", matches[0]);
    }

    [Fact]
    public void FindSavedVariablesFiles_UnrelatedFolder_ReturnsEmpty()
    {
        var matches = SavedVariablesLocator.FindSavedVariablesFiles(Path.GetTempPath());

        Assert.Empty(matches);
    }
}
