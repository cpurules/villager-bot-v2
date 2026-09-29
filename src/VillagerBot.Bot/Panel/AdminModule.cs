using System.Net;
using Microsoft.EntityFrameworkCore;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Ui;
using VillagerBot.Data;

namespace VillagerBot.Bot.Panel;

/// <summary>
/// Admin tools (command-design §4.6). <c>DefaultGuildPermissions = 0</c> hides the command from everyone but server
/// admins until access is granted in Server Settings → Integrations; the precondition is the real check.
/// </summary>
[SlashCommand("admin", "Villager Bot admin tools", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.Admin)]
public sealed class AdminModule(PanelService panel, Staff.CategoryManager categories, VillagerBotDbContext db) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("reset-availability", "Mark every waiting request as not available (members must opt back in)")]
    public async Task ResetAvailabilityAsync()
    {
        var available = await db.ActiveRequests.CountAsync(r => r.PulledAt == null && r.IsAvailable);
        await RespondAsync(new View(
            $"Mark all **{available}** available request{(available == 1 ? "" : "s")} as **not available**? Members will need to set themselves available again.",
            Components: [new ActionRowProperties
            {
                new ButtonProperties(AdminIds.ResetAvailabilityConfirm, "Reset availability", ButtonStyle.Danger),
                new ButtonProperties(AdminIds.Cancel, "Cancel", ButtonStyle.Secondary),
            }]).ToEphemeralReply());
    }

    [SubSlashCommand("sync-categories", "Delete empty overflow request categories the bot created")]
    public async Task SyncCategoriesAsync()
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        var removed = await categories.CleanUpOverflowAsync();
        await ModifyResponseAsync(m => m.Content = removed == 0
            ? "No empty overflow categories to remove."
            : $"Removed {removed} empty overflow {(removed == 1 ? "category" : "categories")}.");
    }

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

public static class AdminIds
{
    public const string ResetAvailabilityConfirm = "admin-reset-availability";
    public const string Cancel = "admin-cancel";
}

public sealed class AdminButtonModule(VillagerBotDbContext db) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(AdminIds.ResetAvailabilityConfirm)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Admin)]
    public async Task ResetAvailabilityAsync()
    {
        var changed = await db.ActiveRequests
            .Where(r => r.PulledAt == null && r.IsAvailable)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsAvailable, false));
        await RespondAsync(new View($"Done: {changed} request{(changed == 1 ? " is" : "s are")} now marked not available.").ToUpdate());
    }

    [ComponentInteraction(AdminIds.Cancel)]
    public Task CancelAsync() => RespondAsync(new View("Cancelled. Nothing was changed.").ToUpdate());
}
