using Microsoft.Extensions.Options;
using NetCord.Rest;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Requests;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Tests;

public class BotTests
{
    private static VillagerBotOptions Options() => new()
    {
        GuildId = 1,
        Roles = new() { Villagers = 10, HavenHunter = 11, Moderator = 12, Haven = 13, HunterHiatus = 14 },
        Channels = new() { Info = 20, Requests = 21, ModMail = 22, StarterKits = 23, StaffLog = 24, TimeoutLog = 25 },
        Categories = new() { Main = 30 },
    };

    [Theory]
    [InlineData(new ulong[] { }, AccessLevel.Member, true)]
    [InlineData(new ulong[] { }, AccessLevel.Villager, false)]
    [InlineData(new ulong[] { 10 }, AccessLevel.Villager, true)]
    [InlineData(new ulong[] { 14 }, AccessLevel.StaffReadOnly, true)]
    [InlineData(new ulong[] { 14 }, AccessLevel.Hunter, false)]
    [InlineData(new ulong[] { 11 }, AccessLevel.Hunter, true)]
    [InlineData(new ulong[] { 12 }, AccessLevel.Hunter, true)]
    [InlineData(new ulong[] { 12 }, AccessLevel.Admin, false)]
    [InlineData(new ulong[] { 13 }, AccessLevel.Admin, true)]
    public void AccessTiersFollowTheDesign(ulong[] roles, AccessLevel level, bool expected)
    {
        var access = new MemberAccess(new RestClient(), Microsoft.Extensions.Options.Options.Create(Options()));
        Assert.Equal(expected, access.Satisfies(roles, level));
    }

    [Fact]
    public void ValidatorAcceptsCompleteConfiguration()
        => Assert.True(new VillagerBotOptionsValidator().Validate(null, Options()).Succeeded);

    [Fact]
    public void ValidatorListsEveryMissingId()
    {
        var options = Options();
        options.Channels.Info = 0;
        options.Roles.Haven = 0;

        var result = new VillagerBotOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("VillagerBot:Channels:Info", result.FailureMessage);
        Assert.Contains("VillagerBot:Roles:Haven", result.FailureMessage);
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(21, "21st")]
    [InlineData(112, "112th")]
    public void OrdinalsReadNaturally(int n, string expected) => Assert.Equal(expected, Markup.Ordinal(n));

    [Fact]
    public void CustomIdsFitDiscordsLimit()
    {
        const ulong maxSnowflake = ulong.MaxValue;
        var longestKey = VillagerCatalog.LoadEmbedded().All.Max(v => v.Key.Length);
        string[] ids =
        [
            $"{RequestIds.Pick}:{maxSnowflake}:{new string('X', longestKey)}",
            $"{RequestIds.Confirm}:{maxSnowflake}:{new string('X', longestKey)}",
            $"{RequestIds.Availability}:{maxSnowflake}:false",
            $"{RequestIds.LeaveConfirm}:{maxSnowflake}",
        ];

        Assert.All(ids, id => Assert.True(id.Length <= 100, $"{id} is {id.Length} characters"));
    }
}
