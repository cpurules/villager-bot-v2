using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using NetCord.Services.ComponentInteractions;
using VillagerBot.Bot.Access;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Bot.Requests;

public sealed class RequestCommandModule(RequestFlow flow) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SlashCommand("request", "Request a villager, or check and manage your current request",
        Contexts = [InteractionContextType.Guild, InteractionContextType.BotDMChannel])]
    [RequireAccess<ApplicationCommandContext>(AccessLevel.Villager)]
    public async Task RequestAsync(
        [SlashCommandParameter(Description = "The villager you'd like (leave empty to see your current request)",
            AutocompleteProviderType = typeof(VillagerAutocompleteProvider), MaxLength = 40)]
        string? villager = null)
    {
        var owner = Context.User.Id;
        await RespondAsync(villager is null
            ? await flow.OpenAsync(owner)
            : (await flow.ChooseAsync(owner, villager)).ToEphemeralReply());
    }
}

/// <summary>Buttons on the panel, the request card, and the confirmation and suggestion prompts.</summary>
public sealed class RequestButtonModule(RequestFlow flow, RequestService requests, RequestViews views, VillagerCatalog catalog)
    : ComponentInteractionModule<ButtonInteractionContext>
{
    [ComponentInteraction(RequestIds.PanelRequest)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task PanelRequestAsync() => await RespondAsync(await flow.OpenAsync(Context.User.Id));

    [ComponentInteraction(RequestIds.PanelCard)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task PanelCardAsync() => await RespondAsync((await flow.CardAsync(Context.User.Id)).ToEphemeralReply());

    [ComponentInteraction(RequestIds.OpenModal)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task OpenModalAsync(ulong owner)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        var existing = await requests.FindAsync(owner);
        if (existing?.PulledAt is not null)
        {
            await RespondAsync((await flow.CardAsync(owner, "Your request has already been picked up by a Haven Hunter, so it can't be changed.")).ToUpdate());
            return;
        }

        await RespondAsync(InteractionCallback.Modal(views.NameModal(owner, changing: existing is not null)));
    }

    [ComponentInteraction(RequestIds.Pick)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task PickAsync(ulong owner, string villagerKey)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        var view = catalog.FindByKey(villagerKey) is { } villager
            ? await flow.ProposeAsync(owner, villager)
            : RequestViews.Message("That villager couldn't be found. Please try again.");
        await RespondAsync(view.ToUpdate());
    }

    [ComponentInteraction(RequestIds.Confirm)]
    [RequireAccess<ButtonInteractionContext>(AccessLevel.Villager)]
    public async Task ConfirmAsync(ulong owner, string villagerKey)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        await RespondAsync((await flow.ConfirmAsync(owner, villagerKey)).ToUpdate());
    }

    [ComponentInteraction(RequestIds.Dismiss)]
    public async Task DismissAsync(ulong owner)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        await RespondAsync(RequestViews.Message("No problem, nothing was changed.").ToUpdate());
    }

    [ComponentInteraction(RequestIds.Card)]
    public async Task CardAsync(ulong owner)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        await RespondAsync((await flow.CardAsync(owner)).ToUpdate());
    }

    [ComponentInteraction(RequestIds.Availability)]
    public async Task AvailabilityAsync(ulong owner, bool available)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        var notice = await requests.SetAvailabilityAsync(owner, available) switch
        {
            RequestChange.Done => available
                ? "You're now marked **available**. Keep an eye on Discord, you'll be pinged when a Haven Hunter picks you!"
                : "You're now marked **not available**. You keep your place in the queue.",
            RequestChange.Pulled => "Your request has already been picked up by a Haven Hunter.",
            _ => null,
        };
        await RespondAsync((await flow.CardAsync(owner, notice)).ToUpdate());
    }

    [ComponentInteraction(RequestIds.Leave)]
    public async Task LeaveAsync(ulong owner)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        var snapshot = await requests.GetSnapshotAsync(owner);
        var view = snapshot switch
        {
            null => views.NoRequest(owner),
            { IsPulled: true } => views.Card(snapshot, "Your request has already been picked up by a Haven Hunter."),
            _ => views.LeaveConfirm(snapshot),
        };
        await RespondAsync(view.ToUpdate());
    }

    [ComponentInteraction(RequestIds.LeaveConfirm)]
    public async Task LeaveConfirmAsync(ulong owner)
    {
        if (await RejectIfNotOwnerAsync(owner))
            return;

        var view = await requests.LeaveAsync(owner) switch
        {
            RequestChange.Done => RequestViews.Message("You've left the queue. You can request a villager again at any time."),
            RequestChange.Pulled => await flow.CardAsync(owner, "Your request has already been picked up by a Haven Hunter."),
            _ => views.NoRequest(owner),
        };
        await RespondAsync(view.ToUpdate());
    }

    private async Task<bool> RejectIfNotOwnerAsync(ulong owner)
    {
        if (Context.User.Id == owner)
            return false;

        await RespondAsync(RequestViews.Message("This isn't your request.").ToEphemeralReply());
        return true;
    }
}

public sealed class RequestModalModule(RequestFlow flow) : ComponentInteractionModule<ModalInteractionContext>
{
    [ComponentInteraction(RequestIds.NameModal)]
    [RequireAccess<ModalInteractionContext>(AccessLevel.Villager)]
    public async Task SubmitNameAsync(ulong owner)
    {
        if (Context.User.Id != owner)
        {
            await RespondAsync(RequestViews.Message("This isn't your request.").ToEphemeralReply());
            return;
        }

        var input = Context.Components.OfType<Label>()
            .Select(label => label.Component)
            .OfType<TextInput>()
            .FirstOrDefault(t => t.CustomId == RequestIds.NameInput)?.Value ?? "";

        await RespondAsync((await flow.ChooseAsync(owner, input)).ToEphemeralReply());
    }
}
