using KARTCompanion.Simulations.QELive;

namespace KARTCompanion.Tests;

public class QELiveGainExtractorTests
{
    private static string LoadFixtureRawBody()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "qelive_report.json");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Parse_DoubleEncodedFixture_DecodesToRealReport()
    {
        var rawBody = LoadFixtureRawBody();

        var report = QELiveReportParser.Parse(rawBody);

        Assert.NotNull(report);
        // Real player from the live fixture (report id efisrltzytyb).
        Assert.Equal("Kandera", report!.PlayerName);
        Assert.Equal("Blackmoore", report.Realm);
        Assert.Equal("Mistweaver Monk", report.Spec);
        Assert.NotEmpty(report.Results);
    }

    [Fact]
    public void Extract_RealFixture_ReadsPercDiffThroughUnchanged()
    {
        var report = QELiveReportParser.Parse(LoadFixtureRawBody())!;
        var importedAt = DateTimeOffset.UtcNow;

        var candidates = QELiveGainExtractor.Extract(report, "heroic-max", importedAt);

        Assert.Equal(report.Results.Count, candidates.Count);

        // Real entry from the live fixture: item 249808 at ilvl 298 has percDiff 1.291 (the
        // biggest upgrade in that report). GainCandidate.GainPct is rounded to 2 decimal places.
        var best = candidates.Single(c => c.ItemId == 249808 && c.Ilvl == 298);
        Assert.Equal(1.29, best.GainPct, precision: 2);
        Assert.Equal("qelive", best.Source);
        Assert.Equal("heroic-max", best.ProfileKey);
    }

    [Fact]
    public void Extract_NoResults_ReturnsEmpty()
    {
        var report = new QELiveReport { PlayerName = "Test", Realm = "Realm" };

        var candidates = QELiveGainExtractor.Extract(report, null, DateTimeOffset.UtcNow);

        Assert.Empty(candidates);
    }
}
