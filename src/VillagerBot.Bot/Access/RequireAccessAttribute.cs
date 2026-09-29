using Microsoft.Extensions.DependencyInjection;
using NetCord.Services;

namespace VillagerBot.Bot.Access;

/// <summary>
/// Enforces an <see cref="AccessLevel"/> in code for any command or component. Discord-side command permissions only
/// control visibility; this is the real check (command-design §2).
/// </summary>
public sealed class RequireAccessAttribute<TContext>(AccessLevel level) : PreconditionAttribute<TContext>
    where TContext : IUserContext, IInteractionContext
{
    public override async ValueTask<PreconditionResult> EnsureCanExecuteAsync(TContext context, IServiceProvider? serviceProvider)
    {
        var access = serviceProvider!.GetRequiredService<MemberAccess>();
        return await access.HasAccessAsync(context.User, context.Interaction.GuildId, level)
            ? PreconditionResult.Success
            : PreconditionResult.Fail(MemberAccess.DeniedMessage(level));
    }
}
