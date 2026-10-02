using VillagerBot.Core.Villagers;

namespace VillagerBot.Tests;

public class VillagerCatalogTests
{
    private static readonly VillagerCatalog Catalog = VillagerCatalog.LoadEmbedded();

    [Fact]
    public void LoadsTheFullList()
    {
        // 413 from the legacy bot plus the four added in the 3.0 update.
        Assert.Equal(417, Catalog.All.Count);
        Assert.All(Catalog.All, v => Assert.False(string.IsNullOrEmpty(v.InternalId)));
    }

    [Theory]
    [InlineData("AGENT_S", "Agent S")]
    [InlineData("RENEE", "Renée")]
    [InlineData("RENÉE", "Renée")] // legacy key as stored in ArangoDB
    [InlineData("raymond", "Raymond")]
    public void FindsByKeyIncludingLegacyKeys(string key, string expectedName)
        => Assert.Equal(expectedName, Catalog.FindByKey(key)?.Name);

    [Theory]
    [InlineData("Mineru", "MINERU", "der12")]
    [InlineData("Tulin", "TULIN", "brd20")]
    [InlineData("viche", "VICHE", "squ20")] // Viché, found without the accent
    [InlineData("Cece", "CECE", "squ19")]
    public void IncludesThe30UpdateVillagers(string name, string key, string internalId)
    {
        var villager = Catalog.FindByName(name);
        Assert.NotNull(villager);
        Assert.Equal((key, internalId, true), (villager.Key, villager.InternalId, villager.Requestable));
    }

    [Theory]
    [InlineData("agent s", "Agent S")]
    [InlineData("AgentS", "Agent S")]
    [InlineData("renee", "Renée")]
    [InlineData("  Raymond ", "Raymond")]
    public void FindsByNameIgnoringCaseAccentsAndPunctuation(string input, string expectedName)
        => Assert.Equal(expectedName, Catalog.FindByName(input)?.Name);

    [Fact]
    public void FindByNameReturnsNullForPartialNames() => Assert.Null(Catalog.FindByName("Raymon"));

    [Fact]
    public void SearchRanksPrefixMatchesFirst()
    {
        var results = Catalog.Search("ray", 5);
        Assert.Equal("Raymond", results[0].Name);
    }

    [Fact]
    public void SearchSuggestsCloseMisspellings()
        => Assert.Contains(Catalog.Search("Raymnod", 5), v => v.Name == "Raymond");

    [Theory]
    [InlineData("MARTY")]
    [InlineData("TOBY")]
    [InlineData("ETOILE")]
    [InlineData("CHELSEA")]
    [InlineData("CHAI")]
    [InlineData("RILLA")]
    public void SanrioVillagersCanBeRequested(string key) => Assert.True(Catalog.FindByKey(key)!.Requestable);

    [Fact]
    public void EveryVillagerIsCurrentlyRequestable() => Assert.All(Catalog.All, v => Assert.True(v.Requestable));

    [Fact]
    public void RequestableSearchLeavesOutBlockedVillagers()
    {
        // No real villager is blocked right now, so exercise the flag with a small made-up catalogue.
        var catalog = new VillagerCatalog(
        [
            new Villager("MARTY", "Marty", "Male", "Cub", "Lazy", "pompom", "cbr18", [], Requestable: false),
            new Villager("MARSHAL", "Marshal", "Male", "Squirrel", "Smug", "sulky", "squ17", []),
        ]);

        Assert.Contains(catalog.Search("Mar", 5), v => v.Name == "Marty");
        Assert.DoesNotContain(catalog.Search("Mar", 5, requestableOnly: true), v => v.Name == "Marty");
        Assert.Contains(catalog.Search("Mar", 5, requestableOnly: true), v => v.Name == "Marshal");
    }

    [Fact]
    public void SearchRespectsLimit() => Assert.Equal(25, Catalog.Search("", 25).Count);
}
