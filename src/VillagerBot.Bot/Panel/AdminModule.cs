using System.Net;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Bot.Access;

namespace VillagerBot.Bot.Panel;

/// <summary>
/// Admin tools (command-design §4.6). <c>DefaultGuildPermissions = 0</c> hides the command from everyone but server
/// admins until access is granted in Server Settings → Integrations; the precondition is the real check.
/// </summary>
[SlashCommand("admin", "Villager Bot admin tools", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.Admin)]
public sealed class AdminModule(PanelService panel) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("panel", "Post or refresh the villager request panel in the info channel")]
    public async Task PanelAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));

        string reply;
        try
        {
            var (channelId, messageId, created) = await panel.PublishAsync();
            var link = $"https://discord.com/channels/{Context.Interaction.GuildId}/{channelId}/{messageId}";
            reply = created ? $"Posted the panel: {link}" : $"Refreshed the panel: {link}";
        }
        catch (RestException e) when (e.StatusCode == HttpStatusCode.Forbidden)
        {
            reply = $"I don't have access to the info channel ({e.Error?.Message ?? "Forbidden"}). " +
                    "Give my role **View Channel**, **Send Messages** and **Embed Links** there, then try again.";
        }

        await ModifyResponseAsync(m => m.Content = reply);
    }
}
