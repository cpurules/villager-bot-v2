using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Bot.Requests;

/// <summary>Type-ahead for villager options (all villagers, for staff commands). The choice value is the catalogue key.</summary>
public sealed class VillagerAutocompleteProvider(VillagerCatalog catalog) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var choices = catalog.Search(option.Value ?? "", 25)
            .Select(v => new ApplicationCommandOptionChoiceProperties(v.Name, v.Key));
        return new(choices);
    }
}

/// <summary>Type-ahead for member requests: only villagers that can currently be requested.</summary>
public sealed class RequestableVillagerAutocompleteProvider(VillagerCatalog catalog) : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var choices = catalog.Search(option.Value ?? "", 25, requestableOnly: true)
            .Select(v => new ApplicationCommandOptionChoiceProperties(v.Name, v.Key));
        return new(choices);
    }
}
