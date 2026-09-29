using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Configuration;
using VillagerBot.Bot.Requests;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

/// <summary><c>/queue list</c> (command-design §4.1): public, requests channel only.</summary>
[SlashCommand("queue", "Browse the villager request queue", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.StaffReadOnly)]
public sealed class QueueModule(VillagerBotDbContext db, VillagerGroups groups, StaffViews views, IOptions<VillagerBotOptions> options)
    : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("list", "Post the next available requests in the requests channel")]
    public async Task ListAsync(
        [SlashCommandParameter(Description = "Only show this villager or group (e.g. Sanrio)",
            AutocompleteProviderType = typeof(VillagerOrGroupAutocompleteProvider), MaxLength = 40)]
        string? villager = null,
        [SlashCommandParameter(Description = "Hide that villager or group instead")]
        bool exclude = false)
    {
        var requestsChannel = options.Value.Channels.Requests;
        if (Context.Channel.Id != requestsChannel)
        {
            await RespondAsync(new View($"Use this in <#{requestsChannel}>.").ToEphemeralReply());
            return;
        }

        VillagerFilter? filter = null;
        if (villager is not null && (filter = groups.Resolve(villager, exclude)) is null)
        {
            await RespondAsync(new View($"I don't know a villager or group called **{villager}**.").ToEphemeralReply());
            return;
        }

        var query = db.ActiveRequests.AsNoTracking().Where(r => r.PulledAt == null && r.IsAvailable);
        if (filter is not null)
            query = filter.Apply(query);

        var total = await query.CountAsync();
        var page = await query.OrderBy(r => r.QueuePosition).Take(StaffViews.QueuePageSize).ToListAsync();
        await RespondAsync(InteractionCallback.Message(views.QueueList(page, total, filter)));
    }
}

/// <summary><c>/pull user|villager|next</c> (command-design §4.2).</summary>
[SlashCommand("pull", "Pull villager requests and open their channels", DefaultGuildPermissions = (Permissions)0,
    Contexts = [InteractionContextType.Guild])]
[RequireAccess<ApplicationCommandContext>(AccessLevel.Hunter)]
public sealed class PullModule(PullService pulls, PullReporter reporter, VillagerGroups groups, VillagerCatalog catalog,
    IOptions<VillagerBotOptions> options) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("user", "Pull a specific member's request (they don't need to be marked available)")]
    public async Task UserAsync(
        [SlashCommandParameter(Description = "The member")] User? user = null,
        [SlashCommandParameter(Name = "user-id", Description = "Or their user ID, if they're hard to find")] string? userId = null)
    {
        if (UserOptions.Resolve(user, userId, out var targetId) is { } problem)
        {
            await RespondAsync(new View(problem).ToEphemeralReply());
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        var result = await pulls.PullUserAsync(Context.User.Id, targetId);
        await FinishAsync([result], "");
    }

    [SubSlashCommand("villager", "Pull the next available requests for a villager")]
    public async Task VillagerAsync(
        [SlashCommandParameter(Description = "The villager", AutocompleteProviderType = typeof(VillagerAutocompleteProvider), MaxLength = 40)]
        string villager,
        [SlashCommandParameter(Description = "How many to pull", MinValue = 1, MaxValue = 10)] int count = 1)
    {
        if ((catalog.FindByKey(villager.Trim()) ?? catalog.FindByName(villager)) is not { } target)
        {
            await RespondAsync(new View($"I don't know a villager called **{villager}**.").ToEphemeralReply());
            return;
        }

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        var results = await pulls.PullNextAsync(Context.User.Id, Clamp(count), q => q.Where(r => r.VillagerKey == target.Key));
        await FinishAsync(results, $"There are no available requests for **{target.Name}**.");
    }

    [SubSlashCommand("next", "Pull the next available requests of any villager (Sanrio villagers excluded)")]
    public async Task NextAsync(
        [SlashCommandParameter(Description = "How many to pull", MinValue = 1, MaxValue = 10)] int count = 1)
    {
        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        var excluded = groups.BulkExcluded.ToList();
        var results = await pulls.PullNextAsync(Context.User.Id, Clamp(count), q => q.Where(r => !excluded.Contains(r.VillagerKey)));
        await FinishAsync(results, "There are no available requests right now.");
    }

    private int Clamp(int count) => Math.Clamp(count, 1, options.Value.Pull.MaxCount);

    private async Task FinishAsync(IReadOnlyList<PullResult> results, string nothingMessage)
    {
        var summary = await reporter.ReportAsync(Context.User.Id, results, nothingMessage);
        await ModifyResponseAsync(m =>
        {
            m.Content = summary;
            m.AllowedMentions = AllowedMentionsProperties.None;
        });
    }
}

/// <summary>The "Pull a request…" menu on the public queue list.</summary>
public sealed class QueueMenuModule(PullService pulls, PullReporter reporter) : ComponentInteractionModule<StringMenuInteractionContext>
{
    [ComponentInteraction(StaffIds.QueuePull)]
    [RequireAccess<StringMenuInteractionContext>(AccessLevel.Hunter)]
    public async Task PullFromListAsync()
    {
        if (Context.SelectedValues.Count == 0 || !ulong.TryParse(Context.SelectedValues[0], out var userId))
            return;

        await RespondAsync(InteractionCallback.DeferredMessage(MessageFlags.Ephemeral));
        var result = await pulls.PullUserAsync(Context.User.Id, userId);
        var summary = await reporter.ReportAsync(Context.User.Id, [result], "");
        await ModifyResponseAsync(m =>
        {
            m.Content = summary;
            m.AllowedMentions = AllowedMentionsProperties.None;
        });
    }
}

/// <summary><c>/close</c> from inside a request channel (command-design §4.3).</summary>
public sealed class CloseCommandModule(CloseService closer) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("close", "Close this request channel", DefaultGuildPermissions = (Permissions)0, Contexts = [InteractionContextType.Guild])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.Hunter)]
    public async Task CloseAsync([SlashCommandParameter(Description = "How did it go?")] CloseOutcome outcome)
    {
        if (await closer.FindByChannelAsync(Context.Channel.Id) is null)
        {
            await RespondAsync(new View("This isn't an open request channel.").ToEphemeralReply());
            return;
        }

        if (outcome == CloseOutcome.Timeout)
        {
            await RespondAsync(StaffViews.TimeoutConfirm().ToEphemeralReply());
            return;
        }

        await RespondAsync(new View($"✅ Closing as completed (by <@{Context.User.Id}>)…").ToEphemeralReply());
        await closer.CloseAsync(Context.Channel.Id, CloseOutcome.Completed, Context.User.Id);
    }
}

/// <summary>The close buttons on the welcome message, and the timeout confirmation.</summary>
public sealed class CloseButtonModule(CloseService closer) : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(StaffIds.CloseCompleted)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Hunter)]
    public async Task CompletedAsync()
    {
        if (await RejectIfNotRequestChannelAsync())
            return;

        await RespondAsync(new View($"✅ Closing as completed (by <@{Context.User.Id}>)…").ToEphemeralReply());
        await closer.CloseAsync(Context.Channel.Id, CloseOutcome.Completed, Context.User.Id);
    }

    [ComponentInteraction(StaffIds.CloseTimeout)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Hunter)]
    public async Task TimeoutAsync()
    {
        if (await RejectIfNotRequestChannelAsync())
            return;

        await RespondAsync(StaffViews.TimeoutConfirm().ToEphemeralReply());
    }

    [ComponentInteraction(StaffIds.CloseTimeoutConfirm)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Hunter)]
    public async Task TimeoutConfirmAsync()
    {
        if (await closer.FindByChannelAsync(Context.Channel.Id) is null)
        {
            await RespondAsync(new View("This request has already been closed.").ToUpdate());
            return;
        }

        await RespondAsync(new View("⏱️ Closing as timed out…").ToUpdate());
        await closer.CloseAsync(Context.Channel.Id, CloseOutcome.Timeout, Context.User.Id);
    }

    [ComponentInteraction(StaffIds.CloseCancel)]
    public Task CancelAsync() => RespondAsync(new View("Cancelled. The request is still open.").ToUpdate());

    private async Task<bool> RejectIfNotRequestChannelAsync()
    {
        if (await closer.FindByChannelAsync(Context.Channel.Id) is not null)
            return false;

        await RespondAsync(new View("This request has already been closed.").ToEphemeralReply());
        return true;
    }
}

/// <summary>Shared handling for commands that take a user picker <em>or</em> a raw user ID (B14).</summary>
public static class UserOptions
{
    /// <summary>Returns a problem message, or null with <paramref name="userId"/> set.</summary>
    public static string? Resolve(User? user, string? rawId, out ulong userId)
    {
        userId = 0;
        if (user is not null && !string.IsNullOrWhiteSpace(rawId))
            return "Give either a member or a user ID, not both.";
        if (user is not null)
        {
            userId = user.Id;
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawId))
            return "Give a member or a user ID.";

        var digits = rawId.Trim().Trim('<', '>', '@', '!');
        return ulong.TryParse(digits, out userId) ? null : $"`{rawId}` isn't a valid user ID.";
    }
}
