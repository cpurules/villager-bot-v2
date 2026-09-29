using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

/// <summary>
/// Periodically archives requests from members who have left the server (B8, command-design §7). Uses one REST member
/// lookup per open request instead of the privileged Server Members intent. Pulled requests also get their channel
/// deleted and a note in the staff log.
/// </summary>
public sealed class DepartedMemberCleanup(IServiceScopeFactory scopes, IOptions<VillagerBotOptions> options, ILogger<DepartedMemberCleanup> logger)
    : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PerLookupDelay = TimeSpan.FromMilliseconds(250);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Jobs.DepartedCleanupEnabled)
        {
            logger.LogInformation("Departed-member cleanup is disabled (VillagerBot:Jobs:DepartedCleanupEnabled).");
            return;
        }

        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.Jobs.DepartedCleanupHours));
        await Task.Delay(StartupDelay, stoppingToken);

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Departed-member cleanup failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<VillagerBotDbContext>();
        var access = services.GetRequiredService<MemberAccess>();

        var userIds = await db.ActiveRequests.AsNoTracking().Select(r => r.UserId).ToListAsync(cancellationToken);
        var removed = 0;

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await access.IsMemberAsync(userId))
            {
                await RemoveAsync(services, db, userId);
                removed++;
            }

            await Task.Delay(PerLookupDelay, cancellationToken);
        }

        logger.LogInformation("Departed-member cleanup: checked {Count} requests, removed {Removed}.", userIds.Count, removed);
        return removed;
    }

    private async Task RemoveAsync(IServiceProvider services, VillagerBotDbContext db, ulong userId)
    {
        // Re-read: the request may have been closed while the check was running.
        if (await db.ActiveRequests.FirstOrDefaultAsync(r => r.UserId == userId) is not { } request)
            return;

        await db.ArchiveAsync(request, ArchiveOutcome.Removed, services.GetRequiredService<TimeProvider>().GetUtcNow());
        if (request.ChannelId is not { } channelId)
            return;

        // A pulled request's channel goes too, like the old "user left the queue or the server" handling.
        var rest = services.GetRequiredService<RestClient>();
        var villager = services.GetRequiredService<VillagerCatalog>().FindByKey(request.VillagerKey)?.Name ?? request.VillagerKey;
        ulong? parentId = null;
        try
        {
            parentId = (await rest.GetChannelAsync(channelId)) is TextGuildChannel text ? text.ParentId : null;
            await rest.DeleteChannelAsync(channelId);
        }
        catch (RestException e)
        {
            logger.LogWarning(e, "Couldn't delete request channel {ChannelId} for departed member {UserId}.", channelId, userId);
        }

        try
        {
            await rest.SendMessageAsync(options.Value.Channels.StaffLog, new MessageProperties
            {
                Content = $"<@{userId}> left the server, so their pulled request for **{villager}** was removed and its channel closed.",
                AllowedMentions = AllowedMentionsProperties.None,
            });
        }
        catch (RestException e)
        {
            logger.LogWarning(e, "Couldn't post to the staff log.");
        }

        await services.GetRequiredService<CategoryManager>().DeleteIfEmptyOverflowAsync(parentId);
    }
}
