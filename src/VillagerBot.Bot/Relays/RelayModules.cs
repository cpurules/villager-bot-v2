using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Ui;

namespace VillagerBot.Bot.Relays;

/// <summary>Custom IDs for the mod mail and starter kit flows (command-design §3.4–3.5).</summary>
public static class RelayIds
{
    public const string ModMailModal = "modmail";
    public const string ModMailInput = "message";
    public const string StarterKitYes = "starterkit-yes"; // :owner
    public const string StarterKitNo = "starterkit-no";   // :owner
}

/// <summary>Posts member submissions to staff channels.</summary>
public sealed class RelayService(RestClient rest, IOptions<VillagerBotOptions> options, TimeProvider time)
{
    private static readonly Color RelayColor = new(0x82FFD1);
    private readonly VillagerBotOptions _options = options.Value;

    public Task SendModMailAsync(User user, string message)
    {
        var emoji = Markup.Emoji(_options.Emoji.ModMail, "📬");
        return PostAsync(_options.Channels.ModMail, user, $"{emoji} You've got mail", message);
    }

    public Task SendStarterKitRequestAsync(User user)
    {
        var emoji = Markup.Emoji(_options.Emoji.StarterKit, "🎁");
        return PostAsync(_options.Channels.StarterKits, user, $"{emoji} Starter Kit Request Received", description: null);
    }

    private Task PostAsync(ulong channelId, User user, string title, string? description) =>
        rest.SendMessageAsync(channelId, new MessageProperties
        {
            // The mention above the embed makes the sender clickable; it deliberately doesn't ping anyone.
            Content = $"<@{user.Id}>",
            AllowedMentions = AllowedMentionsProperties.None,
            Embeds =
            [
                new EmbedProperties
                {
                    Title = title,
                    Description = description,
                    Thumbnail = (user.GetAvatarUrl() ?? user.DefaultAvatarUrl).ToString(),
                    Fields =
                    [
                        new EmbedFieldProperties { Name = "From", Value = $"<@{user.Id}> - {user.Username}" },
                        new EmbedFieldProperties { Name = "User Id", Value = user.Id.ToString() },
                    ],
                    Timestamp = time.GetUtcNow(),
                    Color = RelayColor,
                },
            ],
        });
}

public sealed class RelayCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("modmail", "Send a private message to the mod team",
        Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.Member)]
    public Task ModMailAsync() => RespondAsync(InteractionCallback.Modal(
        new ModalProperties(RelayIds.ModMailModal, "Message the mod team")
            .AddComponents(new LabelProperties("Your message",
                new TextInputProperties(RelayIds.ModMailInput, TextInputStyle.Paragraph)
                    .WithMinLength(1)
                    .WithMaxLength(4000)))));

    [SlashCommand("starterkit", "Request a starter kit (for new players with 80 hours or less of play time)",
        Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.Villager)]
    public Task StarterKitAsync()
    {
        var owner = Context.User.Id;
        var view = new View(
            "Starter kits are available for new players with **80 hours or less** of play time. Would you like to request one?",
            Components: [new ActionRowProperties
            {
                new ButtonProperties($"{RelayIds.StarterKitYes}:{owner}", "Request a starter kit", EmojiProperties.Standard("🎁"), ButtonStyle.Success),
                new ButtonProperties($"{RelayIds.StarterKitNo}:{owner}", "Cancel", ButtonStyle.Secondary),
            }]);
        return RespondAsync(view.ToEphemeralReply());
    }
}

public sealed class RelayModalModule(RelayService relays) : ComponentInteractionModule<ModalInteractionContext>
{
    [ComponentInteraction(RelayIds.ModMailModal)]
    [RequireAccess<ModalInteractionContext>(AccessLevel.Member)]
    public async Task SubmitModMailAsync()
    {
        var message = Context.Components.OfType<Label>()
            .Select(label => label.Component)
            .OfType<TextInput>()
            .FirstOrDefault(t => t.CustomId == RelayIds.ModMailInput)?.Value;

        if (string.IsNullOrWhiteSpace(message))
        {
            await RespondAsync(new View("Your message was empty, so nothing was sent.").ToEphemeralReply());
            return;
        }

        await relays.SendModMailAsync(Context.User, message);
        await RespondAsync(new View("📬 Your message has been sent to the mod team!").ToEphemeralReply());
    }
}

public sealed class RelayButtonModule(RelayService relays) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(RelayIds.StarterKitYes)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task StarterKitYesAsync(ulong owner)
    {
        if (Context.User.Id != owner)
            return;

        await relays.SendStarterKitRequestAsync(Context.User);
        await RespondAsync(new View("🎁 Your request for a starter kit has been sent to the Events Team!").ToUpdate());
    }

    [ComponentInteraction(RelayIds.StarterKitNo)]
    public Task StarterKitNoAsync(ulong owner) => RespondAsync(new View(
        "Your starter kit request has been cancelled. Feel free to let a friend know about Villager Haven and the starter kits!").ToUpdate());
}
