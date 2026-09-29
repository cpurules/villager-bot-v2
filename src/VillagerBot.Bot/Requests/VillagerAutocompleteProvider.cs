using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Core.Villagers;

namespace VillagerBot.Bot.Requests;

/// <summary>Type-ahead for villager options. The choice value is the catalogue key.</summary>
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
