using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

public enum CloseOutcome
{
    Completed,

    [NetCord.Services.ApplicationCommands.SlashCommandChoice(Name = "Timed out")]
    Timeout,
}

/// <summary>Closing a pulled request from its channel (command-design §5.3).</summary>
public sealed class CloseService(
    VillagerBotDbContext db,
    RestClient rest,
    CategoryManager categories,
    PullService pulls,
    VillagerCatalog catalog,
    IOptions<VillagerBotOptions> options,
    TimeProvider time,
    ILogger<CloseService> logger)
{
    private readonly VillagerBotOptions _options = options.Value;

    public Task<ActiveRequest?> FindByChannelAsync(ulong channelId)
        => db.ActiveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.ChannelId == channelId);

    /// <summary>
    /// Archives the request, notifies as needed, then deletes the channel (and its overflow category if now empty).
    /// Returns false if the channel isn't an open request channel.
    /// </summary>
    public async Task<bool> CloseAsync(ulong channelId, CloseOutcome outcome, ulong closedBy)
    {
        if (await FindByChannelAsync(channelId) is not { } request)
            return false;

        await db.ArchiveAsync(request, outcome == CloseOutcome.Completed ? ArchiveOutcome.Completed : ArchiveOutcome.Timeout,
            time.GetUtcNow());

        var villagerName = catalog.FindByKey(request.VillagerKey)?.Name ?? request.VillagerKey;
        if (outcome == CloseOutcome.Timeout)
        {
            var dmSent = await pulls.TryDirectMessageAsync(request.UserId, _options.RequestChannel.TimeoutDm);
            await PostAsync(_options.Channels.TimeoutLog,
                $"{Ui.Markup.Emoji(_options.Emoji.Unavailable, "⏱️")} <@{request.UserId}> - {villagerName} (closed by <@{closedBy}>)" +
                (dmSent ? "" : " · couldn't DM them"));
        }

        ulong? parentId = null;
        try
        {
            parentId = (await rest.GetChannelAsync(channelId)) is TextGuildChannel text ? text.ParentId : null;
            await rest.DeleteChannelAsync(channelId);
        }
        catch (RestException e)
        {
            logger.LogWarning(e, "Couldn't delete request channel {ChannelId}.", channelId);
            await PostAsync(_options.Channels.StaffLog,
                $"Closed the request for <@{request.UserId}> ({villagerName}), but couldn't delete <#{channelId}>: {e.Error?.Message ?? e.StatusCode.ToString()}");
        }

        await categories.DeleteIfEmptyOverflowAsync(parentId);
        return true;
    }

    private async Task PostAsync(ulong channelId, string content)
    {
        try
        {
            await rest.SendMessageAsync(channelId, new MessageProperties { Content = content, AllowedMentions = AllowedMentionsProperties.None });
        }
        catch (RestException e)
        {
            logger.LogWarning(e, "Couldn't post to channel {ChannelId}.", channelId);
        }
    }
}
