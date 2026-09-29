using NetCord;
using NetCord.Rest;

namespace VillagerBot.Bot.Ui;

/// <summary>
/// A rendered message that can be sent as a new (ephemeral) reply or applied to an existing message in place, so each
/// screen of a flow is defined once.
/// </summary>
public sealed record View(string? Content, EmbedProperties? Embed = null, IReadOnlyList<IMessageComponentProperties>? Components = null)
{
    public InteractionMessageProperties ToEphemeralMessage() => new()
    {
        Content = Content,
        Embeds = Embed is null ? null : [Embed],
        Components = Components,
        Flags = MessageFlags.Ephemeral,
        AllowedMentions = AllowedMentionsProperties.None,
    };

    public InteractionCallbackProperties ToEphemeralReply() => InteractionCallback.Message(ToEphemeralMessage());

    /// <summary>Replaces the whole message (empty values clear what was there before).</summary>
    public void ApplyTo(MessageOptions message)
    {
        message.Content = Content ?? "";
        message.Embeds = Embed is null ? [] : [Embed];
        message.Components = Components ?? [];
        message.AllowedMentions = AllowedMentionsProperties.None;
    }

    public InteractionCallbackProperties ToUpdate() => InteractionCallback.ModifyMessage(ApplyTo);
}
