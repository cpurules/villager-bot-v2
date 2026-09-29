using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using VillagerBot.Bot.Configuration;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

/// <summary>A set of villagers to include or exclude, from a single villager or a configured group.</summary>
public sealed record VillagerFilter(string Label, IReadOnlySet<string> Keys, bool Exclude)
{
    public IQueryable<ActiveRequest> Apply(IQueryable<ActiveRequest> requests)
    {
        var keys = Keys.ToList();
        return Exclude
            ? requests.Where(r => !keys.Contains(r.VillagerKey))
            : requests.Where(r => keys.Contains(r.VillagerKey));
    }
}

/// <summary>Resolves villager-or-group option values. Groups come from <c>VillagerGroups</c> config.</summary>
public sealed class VillagerGroups(IOptions<VillagerBotOptions> options, VillagerCatalog catalog)
{
    public const string GroupPrefix = "group:";

    public IEnumerable<string> Names => options.Value.VillagerGroups.Keys;

    public IReadOnlySet<string> Group(string name)
        => options.Value.VillagerGroups.FirstOrDefault(g => g.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?
               .ToHashSet(StringComparer.OrdinalIgnoreCase)
           ?? new HashSet<string>();

    public IReadOnlySet<string> BulkExcluded => Group(options.Value.Pull.BulkExcludedGroup);

    /// <summary>Parses an autocomplete value (<c>group:Sanrio</c> or a villager key) or typed text.</summary>
    public VillagerFilter? Resolve(string value, bool exclude)
    {
        var name = value.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase) ? value[GroupPrefix.Length..] : value;
        var groupName = Names.FirstOrDefault(n => n.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (groupName is not null)
            return new VillagerFilter(groupName, Group(groupName), exclude);

        var villager = catalog.FindByKey(value.Trim()) ?? catalog.FindByName(value);
        return villager is null ? null : new VillagerFilter(villager.Name, new HashSet<string> { villager.Key }, exclude);
    }
}

/// <summary>Type-ahead offering configured groups (e.g. Sanrio) as well as villagers.</summary>
public sealed class VillagerOrGroupAutocompleteProvider(VillagerGroups groups, VillagerCatalog catalog)
    : IAutocompleteProvider<AutocompleteInteractionContext>
{
    public ValueTask<IEnumerable<ApplicationCommandOptionChoiceProperties>?> GetChoicesAsync(
        ApplicationCommandInteractionDataOption option,
        AutocompleteInteractionContext context)
    {
        var input = option.Value ?? "";
        var groupChoices = groups.Names
            .Where(n => n.Contains(input.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(n => new ApplicationCommandOptionChoiceProperties($"{n} (group)", VillagerGroups.GroupPrefix + n));
        var villagerChoices = catalog.Search(input, 25)
            .Select(v => new ApplicationCommandOptionChoiceProperties(v.Name, v.Key));

        return new(groupChoices.Concat(villagerChoices).Take(25));
    }
}
