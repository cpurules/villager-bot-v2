using System.Net;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;

namespace VillagerBot.Bot.Access;

/// <summary>Access tiers from command-design §2.</summary>
public enum AccessLevel
{
    /// <summary>Any member of the server.</summary>
    Member,

    /// <summary>Has the Villagers role.</summary>
    Villager,

    /// <summary>Haven Hunter, Moderator, Haven, or Hunter Hiatus (read-only staff commands).</summary>
    StaffReadOnly,

    /// <summary>Haven Hunter, Moderator, or Haven.</summary>
    Hunter,

    /// <summary>Haven (server admin).</summary>
    Admin,
}

/// <summary>
/// Resolves a user's roles in the configured server. Guild interactions already carry the member's roles; interactions
/// from DMs need a REST lookup, which also tells us whether they're still in the server.
/// </summary>
public sealed class MemberAccess(RestClient rest, IOptions<VillagerBotOptions> options)
{
    private readonly VillagerBotOptions _options = options.Value;

    /// <summary>The user's role IDs in the configured server, or null if they aren't a member.</summary>
    public async Task<IReadOnlyList<ulong>?> GetRoleIdsAsync(User user, ulong? interactionGuildId)
    {
        if (user is GuildUser guildUser && interactionGuildId == _options.GuildId)
            return guildUser.RoleIds;

        try
        {
            var member = await rest.GetGuildUserAsync(_options.GuildId, user.Id);
            return member.RoleIds;
        }
        catch (RestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>Whether the user is still in the configured server (REST lookup).</summary>
    public async Task<bool> IsMemberAsync(ulong userId)
    {
        try
        {
            await rest.GetGuildUserAsync(_options.GuildId, userId);
            return true;
        }
        catch (RestException e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<bool> HasAccessAsync(User user, ulong? interactionGuildId, AccessLevel level)
        => await GetRoleIdsAsync(user, interactionGuildId) is { } roles && Satisfies(roles, level);

    public bool Satisfies(IReadOnlyList<ulong> roles, AccessLevel level)
    {
        var r = _options.Roles;
        bool Has(ulong role) => roles.Contains(role);
        var hunter = Has(r.HavenHunter) || Has(r.Moderator) || Has(r.Haven);

        return level switch
        {
            AccessLevel.Member => true,
            AccessLevel.Villager => Has(r.Villagers),
            AccessLevel.StaffReadOnly => hunter || Has(r.HunterHiatus),
            AccessLevel.Hunter => hunter,
            AccessLevel.Admin => Has(r.Haven),
            _ => false,
        };
    }

    public static string DeniedMessage(AccessLevel level) => level switch
    {
        AccessLevel.Member => "You need to be a member of Villager Haven to use this.",
        AccessLevel.Villager => "You need the **Villagers** role to use this. Make sure you've read the server rules and info channels.",
        AccessLevel.Admin => "Only server admins can use this.",
        _ => "Only Haven Hunters can use this.",
    };
}
