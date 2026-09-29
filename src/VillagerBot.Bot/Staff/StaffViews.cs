using System.Text;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

public static class StaffIds
{
    public const string QueuePull = "queue-pull";          // string menu, value = user ID
    public const string CloseCompleted = "close-done";
    public const string CloseTimeout = "close-timeout";
    public const string CloseTimeoutConfirm = "close-timeout-yes";
    public const string CloseCancel = "close-cancel";
}

public sealed class StaffViews(VillagerCatalog catalog)
{
    private static readonly Color ListColor = new(0x40E0D0);

    public const int QueuePageSize = 20;

    private string Name(string key) => catalog.FindByKey(key)?.Name ?? key;

    /// <summary>The close buttons on each request channel's welcome message.</summary>
    public static ActionRowProperties CloseButtons() => new()
    {
        new ButtonProperties(StaffIds.CloseCompleted, "Completed", EmojiProperties.Standard("✅"), ButtonStyle.Success),
        new ButtonProperties(StaffIds.CloseTimeout, "Timed out", EmojiProperties.Standard("⏱️"), ButtonStyle.Secondary),
    };

    public static View TimeoutConfirm() => new(
        "Close this request as **timed out**? The member will be DM'd that they missed their pickup, and the channel will be deleted.",
        Components: [new ActionRowProperties
        {
            new ButtonProperties(StaffIds.CloseTimeoutConfirm, "Time out and close", ButtonStyle.Danger),
            new ButtonProperties(StaffIds.CloseCancel, "Cancel", ButtonStyle.Secondary),
        }]);

    /// <summary>The public queue list for the requests channel, with a "Pull a request…" menu (command-design §4.1).</summary>
    public InteractionMessageProperties QueueList(IReadOnlyList<ActiveRequest> requests, int totalAvailable, VillagerFilter? filter)
    {
        var title = filter is null ? "Villager Haven - Request List"
            : filter.Exclude ? $"Villager Haven - Request List (excluding {filter.Label})"
            : $"Villager Haven - Request List ({filter.Label})";

        var description = requests.Count == 0
            ? "No available requests match."
            : string.Join('\n', requests.Select((r, i) =>
                $"**{i + 1}.** <@{r.UserId}> · `{r.UserId}` · **{Name(r.VillagerKey)}** · since {Markup.Timestamp(r.SubmittedAt, 'R')}"));

        var message = new InteractionMessageProperties
        {
            Embeds =
            [
                new EmbedProperties
                {
                    Title = title,
                    Description = description,
                    Color = ListColor,
                    Footer = new EmbedFooterProperties { Text = $"Showing {requests.Count} of {totalAvailable} available requests" },
                    Timestamp = DateTimeOffset.UtcNow,
                },
            ],
            AllowedMentions = AllowedMentionsProperties.None,
        };

        if (requests.Count > 0)
        {
            var menu = new StringMenuProperties(StaffIds.QueuePull) { Placeholder = "Pull a request…" };
            foreach (var (request, i) in requests.Select((r, i) => (r, i)))
            {
                menu.Add(new StringMenuSelectOptionProperties($"{i + 1}. {Name(request.VillagerKey)}", request.UserId.ToString())
                {
                    Description = $"User ID {request.UserId}",
                });
            }

            message.Components = [menu];
        }

        return message;
    }

    /// <summary>The pulling hunter's private summary: channels, internal game IDs, and anything skipped (B6).</summary>
    public string PullSummary(IReadOnlyList<PullResult> results, string nothingMessage)
    {
        var pulled = results.Where(r => r.Status == PullStatus.Pulled).ToList();
        var text = new StringBuilder();

        if (results.Count == 0)
            return nothingMessage;

        if (pulled.Count > 0)
        {
            text.AppendLine(pulled.Count == 1 ? "Pulled 1 request:" : $"Pulled {pulled.Count} requests:");
            foreach (var r in pulled)
            {
                var villager = catalog.FindByKey(r.VillagerKey!);
                text.AppendLine($"• <#{r.ChannelId}> **{villager?.Name ?? r.VillagerKey}** (`{villager?.InternalId}`) for <@{r.UserId}>");
            }

            text.AppendLine();
            text.AppendLine($"Villagers: `{string.Join(' ', pulled.Select(r => Name(r.VillagerKey!)))}`");
            text.AppendLine($"Internal IDs: `{string.Join(' ', pulled.Select(r => catalog.FindByKey(r.VillagerKey!)?.InternalId))}`");
        }

        foreach (var r in results)
        {
            if (r.Status == PullStatus.Pulled && r.Error is not null)
                text.AppendLine($"⚠️ {r.Error}");

            var line = r.Status switch
            {
                PullStatus.Pulled when r.DmFailed => $"⚠️ Couldn't DM <@{r.UserId}> (their DMs are closed). They were pinged in their channel.",
                PullStatus.AlreadyPulled => r.OtherHunterId is { } other
                    ? $"<@{r.UserId}>'s request was already pulled by <@{other}>{(r.ChannelId is { } c ? $" (<#{c}>)" : "")}."
                    : $"<@{r.UserId}>'s request was already pulled.",
                PullStatus.NotFound => $"<@{r.UserId}> doesn't have an open request.",
                PullStatus.MemberLeft => $"<@{r.UserId}> has left the server, so their request for **{Name(r.VillagerKey!)}** was removed.",
                PullStatus.Failed => $"❌ Couldn't pull <@{r.UserId}>'s request: {r.Error}",
                _ => null,
            };
            if (line is not null)
                text.AppendLine(line);
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The public note in the requests channel (decided: yes; no pings).</summary>
    public string? PublicNote(ulong hunterId, IReadOnlyList<PullResult> results)
    {
        var pulled = results.Where(r => r.Status == PullStatus.Pulled).ToList();
        return pulled.Count == 0
            ? null
            : string.Join('\n', pulled.Select(r => $"<@{hunterId}> pulled **{Name(r.VillagerKey!)}** for <@{r.UserId}>"));
    }
}
