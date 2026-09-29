using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;

namespace VillagerBot.Bot.Staff;

/// <summary>Builds the hunter's private pull summary and posts the public note in the requests channel.</summary>
public sealed class PullReporter(StaffViews views, RestClient rest, IOptions<VillagerBotOptions> options, ILogger<PullReporter> logger)
{
    public async Task<string> ReportAsync(ulong hunterId, IReadOnlyList<PullResult> results, string nothingMessage)
    {
        if (views.PublicNote(hunterId, results) is { } note)
        {
            try
            {
                await rest.SendMessageAsync(options.Value.Channels.Requests,
                    new MessageProperties { Content = note, AllowedMentions = AllowedMentionsProperties.None });
            }
            catch (RestException e)
            {
                logger.LogWarning(e, "Couldn't post the pull note in the requests channel.");
            }
        }

        return views.PullSummary(results, nothingMessage);
    }
}
