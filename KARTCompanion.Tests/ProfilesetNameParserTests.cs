using KARTCompanion.Simulations.Raidbots;

namespace KARTCompanion.Tests;

public class ProfilesetNameParserTests
{
    [Fact]
    public void Parse_RealSampleName_ExtractsItemIdIlvlSlotSource()
    {
        // Real profileset name from a live Raidbots report fetched this session (id
        // f2u18PPKqWEQJw9wu963WJ) — see project memory project_droptimizer_gain_feature.
        var result = ProfilesetNameParser.Parse("1307/2735/raid-mythic/249339/279/0/trinket1///");

        Assert.NotNull(result);
        Assert.Equal(249339, result!.ItemId);
        Assert.Equal(279, result.Ilvl);
        Assert.Equal("trinket1", result.Slot);
        Assert.Equal("raid-mythic", result.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-enough/tokens")]
    public void Parse_InvalidInput_ReturnsNull(string name)
    {
        Assert.Null(ProfilesetNameParser.Parse(name));
    }

    [Fact]
    public void Parse_NonNumericItemId_ReturnsNull()
    {
        Assert.Null(ProfilesetNameParser.Parse("1307/2735/raid-mythic/notanumber/279/0/trinket1///"));
    }
}
