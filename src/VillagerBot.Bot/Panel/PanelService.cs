using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Requests;
using VillagerBot.Data;

namespace VillagerBot.Bot.Panel;

/// <summary>
/// The bot-authored request panel in the info channel (command-design §3.1). The message location is stored in
/// <c>bot_state</c> so refreshing edits the same message instead of posting a new one.
/// </summary>
public sealed class PanelService(RestClient rest, VillagerBotDbContext db, IOptions<VillagerBotOptions> options)
{
    private const string ChannelKey = "PanelChannelId";
    private const string MessageKey = "PanelMessageId";
    private static readonly Color PanelColor = new(0x40E0D0);

    private readonly VillagerBotOptions _options = options.Value;

    /// <summary>Edits the existing panel if it's still in the configured channel, otherwise posts a new one.</summary>
    public async Task<(ulong ChannelId, ulong MessageId, bool Created)> PublishAsync()
    {
        var channelId = _options.Channels.Info;
        var embed = new EmbedProperties { Title = _options.Panel.Title, Description = _options.Panel.Description, Color = PanelColor };
        IMessageComponentProperties[] components =
        [
            new ActionRowProperties
            {
                new ButtonProperties(RequestIds.PanelRequest, "Request a villager", EmojiProperties.Standard("🏝️"), ButtonStyle.Primary),
                new ButtonProperties(RequestIds.PanelCard, "My request", EmojiProperties.Standard("📋"), ButtonStyle.Secondary),
                new LinkButtonProperties(_options.Panel.VillagerListUrl, "Villager list", EmojiProperties.Standard("📖")),
            },
        ];

        if (await GetAsync(ChannelKey) == channelId && await GetAsync(MessageKey) is { } messageId)
        {
            try
            {
                await rest.ModifyMessageAsync(channelId, messageId, message =>
                {
                    message.Content = "";
                    message.Embeds = [embed];
                    message.Components = components;
                });
                return (channelId, messageId, false);
            }
            catch (RestException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // The panel was deleted; post a fresh one below.
            }
        }

        var posted = await rest.SendMessageAsync(channelId, new MessageProperties { Embeds = [embed], Components = components });
        await SetAsync(ChannelKey, channelId);
        await SetAsync(MessageKey, posted.Id);
        await db.SaveChangesAsync();
        return (channelId, posted.Id, true);
    }

    private async Task<ulong?> GetAsync(string key)
        => await db.BotState.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key) is { } entry
            && ulong.TryParse(entry.Value, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private async Task SetAsync(string key, ulong value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        if (await db.BotState.FirstOrDefaultAsync(s => s.Key == key) is { } entry)
            entry.Value = text;
        else
            db.BotState.Add(new BotStateEntry { Key = key, Value = text });
    }
}
