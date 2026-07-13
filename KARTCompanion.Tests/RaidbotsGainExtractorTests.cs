using System.Text.Json;
using KARTCompanion.Simulations.Raidbots;

namespace KARTCompanion.Tests;

public class RaidbotsGainExtractorTests
{
    private static RaidbotsReport LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "raidbots_report.json");
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RaidbotsReport>(json)!;
    }

    [Fact]
    public void Extract_RealFixture_ComputesGainPercentAgainstBaseline()
    {
        var report = LoadFixture();
        var importedAt = DateTimeOffset.UtcNow;

        var candidates = RaidbotsGainExtractor.Extract(report, "mythic-max", importedAt);

        Assert.NotEmpty(candidates);

        // Real entry from the live fixture (report id f2u18PPKqWEQJw9wu963WJ): baseline dps
        // 87445.71143826655, item 249339/trinket1 mean 84904.89299802324 → -2.9058...%.
        var trinket = candidates.Single(c => c.ItemId == 249339 && c.Ilvl == 279 && c.Slot == "trinket1");
        Assert.Equal(-2.91, trinket.GainPct, precision: 2);
        Assert.Equal("raidbots", trinket.Source);
        Assert.Equal("mythic-max", trinket.ProfileKey);
    }

    [Fact]
    public void Extract_ZeroBaseline_ReturnsEmpty()
    {
        var report = new RaidbotsReport();
        report.Sim.Players.Add(new RbPlayer { CollectedData = new RbCollectedData { Dps = new RbMetric { Mean = 0 } } });
        report.Sim.Profilesets.Results.Add(new RbProfilesetResult { Name = "1/2/raid-mythic/123/280/0/head///", Mean = 1000 });

        var candidates = RaidbotsGainExtractor.Extract(report, null, DateTimeOffset.UtcNow);

        Assert.Empty(candidates);
    }

    [Fact]
    public void Extract_NoPlayers_ReturnsEmpty()
    {
        var report = new RaidbotsReport();

        var candidates = RaidbotsGainExtractor.Extract(report, null, DateTimeOffset.UtcNow);

        Assert.Empty(candidates);
    }
}
