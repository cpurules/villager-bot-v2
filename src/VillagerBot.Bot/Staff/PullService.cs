using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

public enum PullStatus
{
    Pulled,
    AlreadyPulled,
    NotFound,

    /// <summary>The member left the server; their request was archived as Removed.</summary>
    MemberLeft,

    /// <summary>The request is for a villager that can't currently be distributed (e.g. Sanrio).</summary>
    NotRequestable,
    Failed,
}

public sealed record PullResult(ulong UserId, PullStatus Status, string? VillagerKey = null, ulong? ChannelId = null,
    bool DmFailed = false, ulong? OtherHunterId = null, string? Error = null);

/// <summary>
/// Pulling requests (command-design §5.1). Each request is claimed with a conditional update before its channel is
/// created, so two hunters racing for the same request can't both get it.
/// </summary>
public sealed class PullService(
    VillagerBotDbContext db,
    RestClient rest,
    CategoryManager categories,
    MemberAccess access,
    VillagerCatalog catalog,
    IOptions<VillagerBotOptions> options,
    TimeProvider time,
    ILogger<PullService> logger)
{
    private readonly VillagerBotOptions _options = options.Value;

    /// <summary>Pulls one member's request whether or not they're marked available (matches the old <c>!select &lt;id&gt;</c>).</summary>
    public Task<PullResult> PullUserAsync(ulong hunterId, ulong userId) => PullOneAsync(hunterId, userId);

    /// <summary>Pulls up to <paramref name="count"/> of the next available requests matching the filter, in queue order.</summary>
    public async Task<IReadOnlyList<PullResult>> PullNextAsync(ulong hunterId, int count, Func<IQueryable<ActiveRequest>, IQueryable<ActiveRequest>> filter)
    {
        var results = new List<PullResult>();
        var tried = new HashSet<ulong>();

        while (results.Count(r => r.Status == PullStatus.Pulled) < count)
        {
            var remaining = count - results.Count(r => r.Status == PullStatus.Pulled);
            var skip = tried.ToList();
            var candidates = await filter(db.ActiveRequests.AsNoTracking().Where(r => r.PulledAt == null && r.IsAvailable))
                .Where(r => !skip.Contains(r.UserId))
                .OrderBy(r => r.QueuePosition)
                .Select(r => r.UserId)
                .Take(remaining)
                .ToListAsync();
            if (candidates.Count == 0)
                break;

            foreach (var userId in candidates)
            {
                tried.Add(userId);
                var result = await PullOneAsync(hunterId, userId);
                // Lost races are silent in bulk pulls; the next candidate takes their place.
                if (result.Status is not (PullStatus.AlreadyPulled or PullStatus.NotFound))
                    results.Add(result);

                // A channel failure (e.g. missing category or permissions) would repeat for every candidate.
                if (result.Status == PullStatus.Failed)
                    return results;
            }
        }

        return results;
    }

    private async Task<PullResult> PullOneAsync(ulong hunterId, ulong userId)
    {
        if (await db.ActiveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId) is not { } request)
            return new PullResult(userId, PullStatus.NotFound);
        if (request.PulledAt is not null)
            return new PullResult(userId, PullStatus.AlreadyPulled, request.VillagerKey, request.ChannelId, OtherHunterId: request.HunterId);
        if (catalog.FindByKey(request.VillagerKey) is not { Requestable: true })
            return new PullResult(userId, PullStatus.NotRequestable, request.VillagerKey);

        if (!await access.IsMemberAsync(userId))
        {
            await db.ArchiveAsync(request, ArchiveOutcome.Removed, time.GetUtcNow());
            return new PullResult(userId, PullStatus.MemberLeft, request.VillagerKey);
        }

        var claimed = await db.ActiveRequests
            .Where(r => r.UserId == userId && r.PulledAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PulledAt, time.GetUtcNow())
                .SetProperty(r => r.HunterId, hunterId));
        if (claimed == 0)
            return new PullResult(userId, PullStatus.AlreadyPulled, request.VillagerKey);

        var villagerName = catalog.FindByKey(request.VillagerKey)?.Name ?? request.VillagerKey;
        ulong channelId;
        try
        {
            channelId = await categories.CreateRequestChannelAsync(villagerName, userId, hunterId);
        }
        catch (Exception e)
        {
            // Give the request back to the queue so it isn't stranded without a channel.
            await db.ActiveRequests.Where(r => r.UserId == userId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.PulledAt, (DateTimeOffset?)null)
                .SetProperty(r => r.HunterId, (ulong?)null));
            logger.LogError(e, "Creating the request channel for {UserId} failed.", userId);
            var error = e is RequestChannelException ? e.Message
                : e is RestException rest ? $"Discord refused to create the channel ({rest.Error?.Message ?? rest.StatusCode.ToString()}). Check my permissions on the request category."
                : "Something went wrong creating the channel.";
            return new PullResult(userId, PullStatus.Failed, request.VillagerKey, Error: error);
        }

        await db.ActiveRequests.Where(r => r.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(r => r.ChannelId, channelId));

        string? warning = null;
        try
        {
            await rest.SendMessageAsync(channelId, new MessageProperties
            {
                Content = Fill(_options.RequestChannel.Welcome, userId, hunterId, villagerName, channelId),
                Components = [StaffViews.CloseButtons()],
                // Distinct: Discord rejects duplicates, e.g. when a hunter pulls their own request.
                AllowedMentions = new AllowedMentionsProperties { AllowedUsers = new[] { userId, hunterId }.Distinct() },
            });
        }
        catch (RestException e)
        {
            // The request is already pulled and has its channel; don't strand it over the welcome message.
            logger.LogError(e, "Posting the welcome message in {ChannelId} failed.", channelId);
            warning = $"Couldn't post the welcome message in <#{channelId}> ({e.Error?.Message ?? e.StatusCode.ToString()}). " +
                      "The close buttons are missing, so use `/close` there.";
        }

        var dmFailed = !await TryDirectMessageAsync(userId, Fill(_options.RequestChannel.PulledDm, userId, hunterId, villagerName, channelId));
        return new PullResult(userId, PullStatus.Pulled, request.VillagerKey, channelId, dmFailed, Error: warning);
    }

    /// <summary>Returns false when the member doesn't accept DMs from the bot (B6: the caller tells the hunter).</summary>
    public async Task<bool> TryDirectMessageAsync(ulong userId, string content)
    {
        try
        {
            var dm = await rest.GetDMChannelAsync(userId);
            await rest.SendMessageAsync(dm.Id, new MessageProperties { Content = content, AllowedMentions = AllowedMentionsProperties.None });
            return true;
        }
        catch (RestException e)
        {
            logger.LogInformation("Couldn't DM {UserId}: {Status} {Message}", userId, e.StatusCode, e.Error?.Message);
            return false;
        }
    }

    public static string Fill(string template, ulong memberId, ulong hunterId, string villager, ulong channelId) => template
        .Replace("{member}", $"<@{memberId}>")
        .Replace("{hunter}", $"<@{hunterId}>")
        .Replace("{villager}", villager)
        .Replace("{channel}", $"<#{channelId}>");
}
