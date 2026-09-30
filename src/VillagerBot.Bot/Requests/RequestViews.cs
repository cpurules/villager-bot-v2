using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Bot.Requests;

/// <summary>
/// Custom IDs for the member request flow. Parameters follow NetCord's <c>name:param:param</c> convention, and every
/// button carries the owner's user ID so handlers can refuse clicks from anyone else.
/// </summary>
public static class RequestIds
{
    public const string PanelRequest = "panel-request";
    public const string PanelCard = "panel-card";

    public const string OpenModal = "req-open-modal";   // :owner
    public const string NameModal = "req-name";         // :owner  (modal)
    public const string NameInput = "villager";
    public const string Pick = "req-pick";              // :owner:villagerKey  (a "did you mean" suggestion)
    public const string Confirm = "req-confirm";        // :owner:villagerKey
    public const string Dismiss = "req-dismiss";        // :owner
    public const string Card = "req-card";              // :owner
    public const string Availability = "req-avail";     // :owner:true|false
    public const string Leave = "req-leave";            // :owner
    public const string LeaveConfirm = "req-leave-yes"; // :owner
}

/// <summary>Renders every screen of the member request flow (command-design §3).</summary>
public sealed class RequestViews(IOptions<VillagerBotOptions> options, VillagerCatalog catalog)
{
    private static readonly Color CardColor = new(0x40E0D0);
    private readonly VillagerBotOptions _options = options.Value;

    public string VillagerName(string key) => catalog.FindByKey(key)?.Name ?? key;

    private string AvailableEmoji => Markup.Emoji(_options.Emoji.Available, "🟢");
    private string UnavailableEmoji => Markup.Emoji(_options.Emoji.Unavailable, "🔴");

    public ModalProperties NameModal(ulong owner, bool changing) =>
        new ModalProperties($"{RequestIds.NameModal}:{owner}", changing ? "Change your villager" : "Request a villager")
            .AddComponents(new LabelProperties("Villager name",
                new TextInputProperties(RequestIds.NameInput, TextInputStyle.Short)
                    .WithPlaceholder("e.g. Raymond")
                    .WithMinLength(1)
                    .WithMaxLength(40)));

    /// <summary>The request card. Read-only (no buttons) once a Haven Hunter has pulled the request.</summary>
    public View Card(RequestSnapshot snapshot, string? notice = null)
    {
        var request = snapshot.Request;
        var owner = request.UserId;
        var lines = new List<string>
        {
            $"Villager: **{VillagerName(request.VillagerKey)}**",
            $"Submitted: {Markup.Timestamp(request.SubmittedAt)}",
        };

        if (snapshot.IsPulled)
        {
            lines.Add("");
            lines.Add(request.ChannelId is { } channel
                ? $"🎉 A Haven Hunter is getting your villager ready in <#{channel}>. Head over there!"
                : "🎉 A Haven Hunter is getting your villager ready.");
            return new View(notice, new EmbedProperties { Title = "Your villager request", Description = string.Join('\n', lines), Color = CardColor });
        }

        var requestable = catalog.FindByKey(request.VillagerKey)?.Requestable ?? false;
        lines.Add(request.IsAvailable
            ? $"Status: {AvailableEmoji} **Available**: you have an open plot and can be picked."
            : $"Status: {UnavailableEmoji} **Not available**: you won't be picked until you mark yourself available.");
        lines.Add($"Queue: **{Markup.Ordinal(snapshot.AvailablePosition)}** among available requests · **{Markup.Ordinal(snapshot.OverallPosition)}** overall");
        lines.Add("");
        lines.Add(requestable
            ? "Only mark yourself available when you have an **open plot** and can check Discord. You'll be pinged when your villager is ready."
            : $"⚠️ **{VillagerName(request.VillagerKey)}** can't be requested right now. {_options.NotRequestableReason} " +
              "You can **change villager** to keep your place in the queue, or leave the queue.");

        var buttons = new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.Availability}:{owner}:true", "I'm available",
                Markup.EmojiProperties(_options.Emoji.Available, "🟢"), ButtonStyle.Success) { Disabled = request.IsAvailable || !requestable },
            new ButtonProperties($"{RequestIds.Availability}:{owner}:false", "Not available",
                Markup.EmojiProperties(_options.Emoji.Unavailable, "🔴"), ButtonStyle.Secondary) { Disabled = !request.IsAvailable },
            new ButtonProperties($"{RequestIds.OpenModal}:{owner}", "Change villager", ButtonStyle.Primary),
            new ButtonProperties($"{RequestIds.Leave}:{owner}", "Leave queue", ButtonStyle.Danger),
        };

        return new View(notice,
            new EmbedProperties { Title = "Your villager request", Description = string.Join('\n', lines), Color = CardColor },
            [buttons]);
    }

    public View NoRequest(ulong owner) => new(
        "You don't have a villager request right now.",
        Components: [new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.OpenModal}:{owner}", "Request a villager", EmojiProperties.Standard("🏝️"), ButtonStyle.Primary),
            new LinkButtonProperties(_options.Panel.VillagerListUrl, "Villager list", EmojiProperties.Standard("📖")),
        }]);

    /// <summary>Shown when someone picks a villager that can't currently be requested (e.g. Sanrio).</summary>
    public View NotRequestable(ulong owner, Villager villager) => new(
        $"Sorry, **{villager.Name}** can't be requested right now. {_options.NotRequestableReason} Please choose a different villager.",
        Components: [new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.OpenModal}:{owner}", "Choose another villager", ButtonStyle.Primary),
            new LinkButtonProperties(_options.Panel.VillagerListUrl, "Villager list", EmojiProperties.Standard("📖")),
            new ButtonProperties($"{RequestIds.Dismiss}:{owner}", "Cancel", ButtonStyle.Secondary),
        }]);

    public View ConfirmCreate(ulong owner, Villager villager) => Confirm(owner, villager, $"Request **{villager.Name}**?");

    public View ConfirmChange(ulong owner, string fromKey, Villager to)
        => Confirm(owner, to, $"Change your request from **{VillagerName(fromKey)}** to **{to.Name}**? You'll keep your place in the queue.");

    private static View Confirm(ulong owner, Villager villager, string question) => new(question,
        Components: [new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.Confirm}:{owner}:{villager.Key}", "Confirm", ButtonStyle.Success),
            new ButtonProperties($"{RequestIds.Dismiss}:{owner}", "Cancel", ButtonStyle.Secondary),
        }]);

    /// <summary>Shown when a typed name doesn't match: up to five suggestions plus Try again.</summary>
    public View NotFound(ulong owner, string input, IReadOnlyList<Villager> suggestions)
    {
        var rows = new List<IMessageComponentProperties>();
        if (suggestions.Count > 0)
        {
            var row = new ActionRowProperties();
            foreach (var villager in suggestions.Take(5))
                row.AddComponents(new ButtonProperties($"{RequestIds.Pick}:{owner}:{villager.Key}", villager.Name, ButtonStyle.Primary));
            rows.Add(row);
        }

        rows.Add(new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.OpenModal}:{owner}", "Try again", ButtonStyle.Secondary),
            new LinkButtonProperties(_options.Panel.VillagerListUrl, "Villager list", EmojiProperties.Standard("📖")),
            new ButtonProperties($"{RequestIds.Dismiss}:{owner}", "Cancel", ButtonStyle.Secondary),
        });

        var shown = input.Length > 40 ? input[..40] + "…" : input;
        return new View(suggestions.Count > 0
            ? $"Couldn't find a villager called **{Sanitize(shown)}**. Did you mean…"
            : $"Couldn't find a villager called **{Sanitize(shown)}**. Check the spelling against the villager list.",
            Components: rows);
    }

    public View LeaveConfirm(RequestSnapshot snapshot) => new(
        $"Leave the queue? You'll lose your place ({Markup.Ordinal(snapshot.OverallPosition)} overall) and your request for **{VillagerName(snapshot.Request.VillagerKey)}**.",
        Components: [new ActionRowProperties
        {
            new ButtonProperties($"{RequestIds.LeaveConfirm}:{snapshot.Request.UserId}", "Leave the queue", ButtonStyle.Danger),
            new ButtonProperties($"{RequestIds.Card}:{snapshot.Request.UserId}", "Keep my request", ButtonStyle.Secondary),
        }]);

    public static View Message(string content) => new(content);

    /// <summary>Stops user input from breaking out of bold text or pinging anyone.</summary>
    private static string Sanitize(string value) => value.Replace("*", "").Replace("`", "").Replace("@", "@​");
}
