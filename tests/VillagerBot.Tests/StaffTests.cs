using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Staff;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Tests;

public class StaffTests
{
    private static readonly VillagerCatalog Catalog = VillagerCatalog.LoadEmbedded();

    private static VillagerGroups Groups() => new(Microsoft.Extensions.Options.Options.Create(new VillagerBotOptions
    {
        VillagerGroups = new() { ["Sanrio"] = ["TOBY", "MARTY", "ETOILE", "CHELSEA", "CHAI", "RILLA"] },
    }), Catalog);

    [Theory]
    [InlineData("Raymond", "request-raymond-")]
    [InlineData("Agent S", "request-agent-s-")]
    [InlineData("Renée", "request-renee-")]
    [InlineData("O'Hare", "request-ohare-")]
    public void ChannelNamesAreDiscordFriendly(string villager, string expectedPrefix)
    {
        var name = CategoryManager.ChannelName(villager);
        Assert.StartsWith(expectedPrefix, name);
        Assert.Matches("^request-[a-z0-9-]+-[0-9a-f]{4}$", name);
    }

    [Theory]
    [InlineData("123456789012345678", 123456789012345678UL)]
    [InlineData(" <@123456789012345678> ", 123456789012345678UL)]
    [InlineData("<@!123456789012345678>", 123456789012345678UL)]
    public void UserIdOptionAcceptsIdsAndMentions(string raw, ulong expected)
    {
        Assert.Null(UserOptions.Resolve(null, raw, out var id));
        Assert.Equal(expected, id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-an-id")]
    public void UserIdOptionRejectsMissingOrInvalidInput(string? raw)
        => Assert.NotNull(UserOptions.Resolve(null, raw, out _));

    [Theory]
    [InlineData("group:Sanrio", "Sanrio", 6)]
    [InlineData("sanrio", "Sanrio", 6)]
    [InlineData("RAYMOND", "Raymond", 1)]
    [InlineData("raymond", "Raymond", 1)]
    public void ResolvesVillagersAndGroups(string value, string label, int keys)
    {
        var filter = Groups().Resolve(value, exclude: false);
        Assert.NotNull(filter);
        Assert.Equal((label, keys), (filter.Label, filter.Keys.Count));
    }

    [Fact]
    public void BulkExclusionDefaultsToSanrio() => Assert.Contains("MARTY", Groups().BulkExcluded);

    [Fact]
    public void PullSummaryListsInternalIdsAndDmFailures()
    {
        var views = new StaffViews(Catalog);
        var summary = views.PullSummary(
        [
            new PullResult(1, PullStatus.Pulled, "RAYMOND", 100, DmFailed: true),
            new PullResult(2, PullStatus.MemberLeft, "AUDIE"),
        ], "nothing");

        Assert.Contains("<#100> **Raymond**", summary);
        Assert.Contains(Catalog.FindByKey("RAYMOND")!.InternalId, summary);
        Assert.Contains("Couldn't DM <@1>", summary);
        Assert.Contains("<@2> has left the server", summary);
        Assert.Equal("nothing", views.PullSummary([], "nothing"));
        Assert.Contains("can't be distributed", views.PullSummary([new PullResult(3, PullStatus.NotRequestable, "MARTY")], "nothing"));
    }
}
