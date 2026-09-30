using NetCord.Rest;
using VillagerBot.Bot.Ui;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Bot.Requests;

/// <summary>
/// The member request flow shared by <c>/request</c>, the panel buttons, and the name modal (command-design §3.2–3.3).
/// Every step re-reads the member's current request, so stale buttons can't act on outdated state.
/// </summary>
public sealed class RequestFlow(RequestService requests, RequestViews views, VillagerCatalog catalog)
{
    public const int SuggestionCount = 5;

    /// <summary>Entry point without a villager: the card if they have a request, otherwise the name modal.</summary>
    public async Task<InteractionCallbackProperties> OpenAsync(ulong owner)
        => await requests.GetSnapshotAsync(owner) is { } snapshot
            ? views.Card(snapshot).ToEphemeralReply()
            : InteractionCallback.Modal(views.NameModal(owner, changing: false));

    public async Task<View> CardAsync(ulong owner, string? notice = null)
        => await requests.GetSnapshotAsync(owner) is { } snapshot ? views.Card(snapshot, notice) : views.NoRequest(owner);

    /// <summary>Resolves typed input (or an autocomplete value, which is the catalogue key) to a confirmation or suggestions.</summary>
    public async Task<View> ChooseAsync(ulong owner, string input)
    {
        var villager = catalog.FindByKey(input.Trim()) ?? catalog.FindByName(input);
        return villager is null
            ? views.NotFound(owner, input.Trim(), catalog.Search(input, SuggestionCount, requestableOnly: true))
            : await ProposeAsync(owner, villager);
    }

    /// <summary>Asks to create, or to change an existing request to, the chosen villager.</summary>
    public async Task<View> ProposeAsync(ulong owner, Villager villager)
    {
        if (!villager.Requestable)
            return views.NotRequestable(owner, villager);

        if (await requests.GetSnapshotAsync(owner) is not { } snapshot)
            return views.ConfirmCreate(owner, villager);

        if (snapshot.IsPulled)
            return views.Card(snapshot, "Your request has already been picked up by a Haven Hunter, so it can't be changed.");

        return snapshot.Request.VillagerKey == villager.Key
            ? views.Card(snapshot, $"You've already requested **{villager.Name}**.")
            : views.ConfirmChange(owner, snapshot.Request.VillagerKey, villager);
    }

    /// <summary>Applies a confirmed choice: creates the request, or changes the villager on an existing one.</summary>
    public async Task<View> ConfirmAsync(ulong owner, string villagerKey)
    {
        if (catalog.FindByKey(villagerKey) is not { } villager)
            return RequestViews.Message("That villager couldn't be found. Please try again.");

        // Re-checked here too: the confirm button may predate the villager being blocked.
        if (!villager.Requestable)
            return views.NotRequestable(owner, villager);

        if (await requests.TryCreateAsync(owner, villager.Key))
        {
            return await CardAsync(owner,
                $"✅ Your request for **{villager.Name}** has been submitted! You'll be pinged when a Haven Hunter is ready for you. " +
                "Mark yourself **available** whenever you have an open plot.");
        }

        return await requests.ChangeVillagerAsync(owner, villager.Key) switch
        {
            RequestChange.Done => await CardAsync(owner, $"✅ Your request has been changed to **{villager.Name}**."),
            RequestChange.Pulled => await CardAsync(owner, "Your request has already been picked up by a Haven Hunter, so it can't be changed."),
            _ => await CardAsync(owner),
        };
    }
}
