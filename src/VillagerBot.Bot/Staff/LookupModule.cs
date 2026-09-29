using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Requests;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

/// <summary>Staff views of any member's request and history (command-design §4.4). Always private.</summary>
public sealed class LookupService(VillagerBotDbContext db, RequestService requests, VillagerCatalog catalog)
{
    private static readonly Color LookupColor = new(0x40E0D0);
    public const int HistoryLimit = 10;

    private string Name(string key) => catalog.FindByKey(key)?.Name ?? key;

    public async Task<View> RequestAsync(ulong userId)
    {
        if (await requests.GetSnapshotAsync(userId) is not { } snapshot)
            return new View($"<@{userId}> doesn't have an open request.");

        var r = snapshot.Request;
        var lines = new List<string>
        {
            $"Member: <@{userId}> (`{userId}`)",
            $"Villager: **{Name(r.VillagerKey)}**",
            $"Submitted: {Markup.Timestamp(r.SubmittedAt)}",
        };

        if (snapshot.IsPulled)
        {
            lines.Add($"Pulled by <@{r.HunterId}> {(r.PulledAt is { } at ? Markup.Timestamp(at, 'R') : "")}" +
                      (r.ChannelId is { } channel ? $" in <#{channel}>" : ""));
        }
        else
        {
            lines.Add($"Status: {(r.IsAvailable ? "**Available**" : "**Not available**")}");
            lines.Add($"Queue: **{Markup.Ordinal(snapshot.AvailablePosition)}** among available · **{Markup.Ordinal(snapshot.OverallPosition)}** overall");
        }

        return new View(null, new EmbedProperties { Title = "Villager request", Description = string.Join('\n', lines), Color = LookupColor });
    }

    public async Task<View> HistoryAsync(ulong userId, string? villagerKey = null)
    {
        var query = db.ArchivedRequests.AsNoTracking().Where(a => a.UserId == userId);
        if (villagerKey is not null)
            query = query.Where(a => a.VillagerKey == villagerKey);

        var total = await query.CountAsync();
        var recent = await query.OrderByDescending(a => a.ClosedAt).Take(HistoryLimit).ToListAsync();
        var active = await db.ActiveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId);

        var scope = villagerKey is null ? "" : $" for **{Name(villagerKey)}**";
        var lines = new List<string> { $"<@{userId}> (`{userId}`) has **{total}** closed request{(total == 1 ? "" : "s")}{scope}." };
        if (active is not null)
            lines.Add($"Open request: **{Name(active.VillagerKey)}**{(active.PulledAt is null ? "" : " (pulled)")}");

        if (recent.Count > 0)
        {
            lines.Add("");
            lines.Add("**Recent history**");
            lines.AddRange(recent.Select(a =>
                $"**{Name(a.VillagerKey)}** · {Outcome(a.Outcome)} {Markup.Timestamp(a.ClosedAt, 'd')}" +
                (a.HunterId is { } hunter ? $" by <@{hunter}>" : "")));
        }

        return new View(null, new EmbedProperties { Title = "Request history", Description = string.Join('\n', lines), Color = LookupColor });
    }

    private static string Outcome(ArchiveOutcome outcome) => outcome switch
    {
        ArchiveOutcome.Completed => "Completed",
        ArchiveOutcome.Timeout => "Timed out",
        _ => "Removed",
    };
}

[SlashCommand("lookup", "Look up a member's villager request or history", DefaultGuildPermissions = (Permissions)0,
    Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.StaffReadOnly)]
public sealed class LookupModule(LookupService lookups, VillagerCatalog catalog) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("request", "Show a member's open request")]
    public async Task RequestAsync(
        [SlashCommandParameter(Description = "The member")] User? user = null,
        [SlashCommandParameter(Name = "user-id", Description = "Or their user ID (works for members who left)")] string? userId = null)
    {
        if (UserOptions.Resolve(user, userId, out var id) is { } problem)
            await RespondAsync(new View(problem).ToEphemeralReply());
        else
            await RespondAsync((await lookups.RequestAsync(id)).ToEphemeralReply());
    }

    [SubSlashCommand("history", "Show a member's closed requests")]
    public async Task HistoryAsync(
        [SlashCommandParameter(Description = "The member")] User? user = null,
        [SlashCommandParameter(Name = "user-id", Description = "Or their user ID (works for members who left)")] string? userId = null,
        [SlashCommandParameter(Description = "Only this villager", AutocompleteProviderType = typeof(VillagerAutocompleteProvider), MaxLength = 40)]
        string? villager = null)
    {
        if (UserOptions.Resolve(user, userId, out var id) is { } problem)
        {
            await RespondAsync(new View(problem).ToEphemeralReply());
            return;
        }

        string? key = null;
        if (villager is not null)
        {
            if ((catalog.FindByKey(villager.Trim()) ?? catalog.FindByName(villager)) is not { } found)
            {
                await RespondAsync(new View($"I don't know a villager called **{villager}**.").ToEphemeralReply());
                return;
            }

            key = found.Key;
        }

        await RespondAsync((await lookups.HistoryAsync(id, key)).ToEphemeralReply());
    }
}

/// <summary>Right-click a member → Apps (command-design §4.4).</summary>
public sealed class LookupUserCommandModule(LookupService lookups) : ApplicationCommandModule<ApplicationCommandContext>
{
    [UserCommand("Request Info", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.StaffReadOnly)]
    public async Task RequestInfoAsync(User user) => await RespondAsync((await lookups.RequestAsync(user.Id)).ToEphemeralReply());

    [UserCommand("Request History", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.StaffReadOnly)]
    public async Task RequestHistoryAsync(User user) => await RespondAsync((await lookups.HistoryAsync(user.Id)).ToEphemeralReply());
}
