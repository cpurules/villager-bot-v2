using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

public enum StatsPeriod
{
    [SlashCommandChoice(Name = "This month")]
    ThisMonth,

    [SlashCommandChoice(Name = "A month")]
    Month,

    [SlashCommandChoice(Name = "A year")]
    Year,

    [SlashCommandChoice(Name = "All time")]
    AllTime,
}

/// <summary><c>/stats</c> (command-design §4.5). Results post publicly in the channel; input errors stay private.</summary>
[SlashCommand("stats", "Villager request statistics", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.StaffReadOnly)]
public sealed class StatsModule(
    VillagerBotDbContext db,
    VillagerGroups groups,
    VillagerCatalog catalog,
    MemberAccess access,
    IOptions<VillagerBotOptions> options,
    TimeProvider time) : ApplicationCommandModule<ApplicationCommandContext>
{
    private static readonly Color StatsColor = new(0x40E0D0);

    [SubSlashCommand("queue", "How many requests are waiting")]
    public async Task QueueAsync(
        [SlashCommandParameter(Description = "Instead count every open request submitted in the last N days", MinValue = 1, MaxValue = 3650)]
        int? days = null)
    {
        string text;
        if (days is { } d)
        {
            var since = time.GetUtcNow().AddDays(-d);
            var count = await db.ActiveRequests.CountAsync(r => r.SubmittedAt >= since);
            text = $"**{count}** open request{Plural(count)} {(count == 1 ? "was" : "were")} submitted in the last {d} day{Plural(d)}.";
        }
        else
        {
            var available = await db.ActiveRequests.CountAsync(r => r.PulledAt == null && r.IsAvailable);
            var waiting = await db.ActiveRequests.CountAsync(r => r.PulledAt == null);
            var pulled = await db.ActiveRequests.CountAsync(r => r.PulledAt != null);
            text = $"**{available}** request{Plural(available)} {(available == 1 ? "is" : "are")} available to pull " +
                   $"({waiting} waiting in total, {pulled} in progress).";
        }

        await RespondAsync(new View(text).ToPublicReply());
    }

    [SubSlashCommand("helped", "How many requests have been completed")]
    public async Task HelpedAsync(
        [SlashCommandParameter(Description = "Only count this villager group (e.g. Sanrio)",
            AutocompleteProviderType = typeof(VillagerOrGroupAutocompleteProvider), MaxLength = 40)]
        string? group = null)
    {
        var query = db.ArchivedRequests.AsNoTracking().Where(a => a.Outcome == ArchiveOutcome.Completed);
        var label = "";
        if (group is not null)
        {
            if (groups.Resolve(group, exclude: false) is not { } filter)
            {
                await RespondAsync(new View($"I don't know a villager or group called **{group}**.").ToEphemeralReply());
                return;
            }

            var keys = filter.Keys.ToList();
            query = query.Where(a => keys.Contains(a.VillagerKey));
            label = $" for **{filter.Label}**";
        }

        var count = await query.CountAsync();
        await RespondAsync(new View($"🎉 **{count:N0}** villager request{Plural(count)} have been completed{label}!").ToPublicReply());
    }

    [SubSlashCommand("top-villagers", "The most requested villagers in the queue")]
    public async Task TopVillagersAsync(
        [SlashCommandParameter(Name = "available-only", Description = "Only count requests marked available")] bool availableOnly = false)
    {
        var query = db.ActiveRequests.AsNoTracking();
        if (availableOnly)
            query = query.Where(r => r.PulledAt == null && r.IsAvailable);

        var top = await query.GroupBy(r => r.VillagerKey)
            .Select(g => new { Key = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Key)
            .Take(25)
            .ToListAsync();

        var lines = top.Select((x, i) => $"**{i + 1}.** {catalog.FindByKey(x.Key)?.Name ?? x.Key} · {x.Count}");
        await RespondAsync(Embed(availableOnly ? "Top villagers (available requests)" : "Top villagers (open requests)", lines).ToPublicReply());
    }

    [SubSlashCommand("top-requesters", "Members with the most closed requests")]
    public async Task TopRequestersAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage());

        // Current members only (as before); check candidates in order until there are ten.
        var candidates = await db.ArchivedRequests.AsNoTracking()
            .GroupBy(a => a.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(40)
            .ToListAsync();

        var lines = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!await access.IsMemberAsync(candidate.UserId))
                continue;
            lines.Add($"**{lines.Count + 1}.** <@{candidate.UserId}> (`{candidate.UserId}`) · {candidate.Count}");
            if (lines.Count == 10)
                break;
        }

        var view = Embed("Top requesters", lines);
        await ModifyResponseAsync(view.ApplyTo);
    }

    [SubSlashCommand("hunters", "Completed requests per Haven Hunter (admins only)")]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.Admin)]
    public async Task HuntersAsync(
        [SlashCommandParameter(Description = "Which period (default: this month)")] StatsPeriod period = StatsPeriod.ThisMonth,
        [SlashCommandParameter(Description = "Year, for 'A month' or 'A year' (default: this year)", MinValue = 2020, MaxValue = 2100)]
        int? year = null,
        [SlashCommandParameter(Description = "Month number, for 'A month' (1-12)", MinValue = 1, MaxValue = 12)] int? month = null)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.StatsTimeZone);
        var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);

        if (period == StatsPeriod.Month && month is null)
        {
            await RespondAsync(new View("Choose a `month` (1-12) for 'A month'.").ToEphemeralReply());
            return;
        }

        (DateTimeOffset? From, DateTimeOffset? To, string Label) range = period switch
        {
            StatsPeriod.ThisMonth => MonthRange(zone, now.Year, now.Month),
            StatsPeriod.Month => MonthRange(zone, year ?? now.Year, month!.Value),
            StatsPeriod.Year => (Local(zone, year ?? now.Year, 1), Local(zone, (year ?? now.Year) + 1, 1), (year ?? now.Year).ToString(CultureInfo.InvariantCulture)),
            _ => (null, null, "all time"),
        };

        var query = db.ArchivedRequests.AsNoTracking().Where(a => a.Outcome == ArchiveOutcome.Completed && a.HunterId != null);
        if (range.From is { } from)
            query = query.Where(a => a.ClosedAt >= from);
        if (range.To is { } to)
            query = query.Where(a => a.ClosedAt < to);

        var rows = await query.GroupBy(a => a.HunterId)
            .Select(g => new { HunterId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();

        var total = rows.Sum(r => r.Count);
        var lines = rows.Select((r, i) => $"**{i + 1}.** <@{r.HunterId}> · {r.Count}").Prepend($"**{total:N0}** completed in total.\n");
        await RespondAsync(Embed($"Haven Hunters: {range.Label}", lines).ToPublicReply());
    }

    private static (DateTimeOffset?, DateTimeOffset?, string) MonthRange(TimeZoneInfo zone, int year, int month)
    {
        var start = Local(zone, year, month);
        var end = month == 12 ? Local(zone, year + 1, 1) : Local(zone, year, month + 1);
        return (start, end, new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture));
    }

    /// <summary>Midnight on the first of the month in the stats time zone, as an absolute instant.</summary>
    private static DateTimeOffset Local(TimeZoneInfo zone, int year, int month)
    {
        var local = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Lists go in the embed description (4,096 characters) rather than a field (B13).</summary>
    private static View Embed(string title, IEnumerable<string> lines)
    {
        var description = string.Join('\n', lines);
        if (description.Length == 0)
            description = "Nothing to show yet.";
        if (description.Length > 4000)
            description = description[..description.LastIndexOf('\n', 4000)] + "\n…";
        return new View(null, new EmbedProperties { Title = title, Description = description, Color = StatsColor });
    }

    private static string Plural(int n) => n == 1 ? "" : "s";
}
