using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetCord.Services;
using VillagerBot.Bot.Configuration;

namespace VillagerBot.Bot.Access;

/// <summary>
/// Limits a staff command to the requests channel (<c>VillagerBot:Channels:Requests</c>), e.g. <c>/queue list</c> and
/// <c>/pull</c>. Elsewhere the user gets a private pointer to the right channel.
/// </summary>
public sealed class RequireRequestsChannelAttribute<TContext> : PreconditionAttribute<TContext>
    where TContext : IChannelContext
{
    public override ValueTask<PreconditionResult> EnsureCanExecuteAsync(TContext context, IServiceProvider? serviceProvider)
    {
        var requestsChannel = serviceProvider!.GetRequiredService<IOptions<VillagerBotOptions>>().Value.Channels.Requests;
        return new(context.Channel?.Id == requestsChannel
            ? PreconditionResult.Success
            : PreconditionResult.Fail($"Use this in <#{requestsChannel}>."));
    }
}
