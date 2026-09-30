using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Rest;

namespace VillagerBot.Bot.Configuration;

/// <summary>
/// Checks once at startup that the bot can see every configured channel and category, and logs exactly what's missing
/// (command-design §6.3). Discord reports a hidden channel only as "Missing Access", so without this a permission gap
/// shows up as a confusing failure the first time someone uses a feature.
/// </summary>
public sealed class StartupCheck(RestClient rest, IOptions<VillagerBotOptions> options, IServiceScopeFactory scopes, ILogger<StartupCheck> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        (string Name, ulong Id)[] targets =
        [
            ("Channels:Info", o.Channels.Info),
            ("Channels:Requests", o.Channels.Requests),
            ("Channels:ModMail", o.Channels.ModMail),
            ("Channels:StarterKits", o.Channels.StarterKits),
            ("Channels:StaffLog", o.Channels.StaffLog),
            ("Channels:TimeoutLog", o.Channels.TimeoutLog),
            ("Categories:Main", o.Categories.Main),
        ];

        var problems = 0;
        foreach (var (name, id) in targets)
        {
            try
            {
                var channel = await rest.GetChannelAsync(id, cancellationToken: stoppingToken);
                if (channel is not NetCord.IGuildChannel { GuildId: var guildId } || guildId != o.GuildId)
                {
                    problems++;
                    logger.LogWarning("Startup check: {Setting} ({Id}) is not in the configured server {GuildId}.", name, id, o.GuildId);
                }
            }
            catch (RestException e) when (e.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                problems++;
                logger.LogWarning(
                    e.StatusCode == HttpStatusCode.NotFound
                        ? "Startup check: {Setting} ({Id}) doesn't exist. Check the ID in appsettings for this environment."
                        : "Startup check: the bot can't see {Setting} ({Id}). Give the bot's role View Channel, Send Messages, " +
                          "Embed Links and Read Message History on that channel (or its category).",
                    name, id);
            }
        }

        if (problems == 0)
            logger.LogInformation("Startup check: all configured channels and categories are visible to the bot.");

        // Villagers that can't be requested (e.g. Sanrio) can't be marked available.
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var changed = await scope.ServiceProvider.GetRequiredService<Requests.RequestService>().EnforceNotRequestableAsync();
            if (changed > 0)
                logger.LogInformation("Startup check: marked {Count} requests for non-requestable villagers as not available.", changed);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Startup check: enforcing non-requestable villagers failed.");
        }

        // Remove overflow categories left empty (e.g. if the bot stopped mid-close).
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var removed = await scope.ServiceProvider.GetRequiredService<Staff.CategoryManager>().CleanUpOverflowAsync();
            if (removed > 0)
                logger.LogInformation("Startup check: removed {Count} empty overflow categories.", removed);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Startup check: overflow category cleanup failed.");
        }
    }
}
