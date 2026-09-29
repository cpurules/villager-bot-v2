using System.Globalization;
using System.Text;
using System.Text.Json;

namespace VillagerBot.Core.Villagers;

/// <summary>
/// The fixed list of requestable villagers, loaded from the embedded <c>villagers.json</c>
/// (generated once from the legacy Java enum).
/// </summary>
public sealed class VillagerCatalog
{
    private const string ResourceName = "VillagerBot.Core.villagers.json";

    private readonly Dictionary<string, Villager> _byKey;
    private readonly Dictionary<string, Villager> _byName;

    public VillagerCatalog(IEnumerable<Villager> villagers)
    {
        All = villagers.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (var villager in All)
        {
            _byKey.Add(villager.Key, villager);
            foreach (var legacyKey in villager.LegacyKeys)
                _byKey.Add(legacyKey, villager);
        }

        _byName = All.ToDictionary(v => Fold(v.Name), StringComparer.Ordinal);
    }

    public IReadOnlyList<Villager> All { get; }

    public static VillagerCatalog LoadEmbedded()
    {
        using var stream = typeof(VillagerCatalog).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        var villagers = JsonSerializer.Deserialize<List<VillagerJson>>(stream, JsonOptions)
            ?? throw new InvalidOperationException("Villager catalogue is empty.");

        return new VillagerCatalog(villagers.Select(v => new Villager(
            v.Key, v.Name, v.Gender, v.Species, v.Personality, v.Catchphrase, v.InternalId, v.LegacyKeys ?? [])));
    }

    /// <summary>Finds a villager by its stored key, including legacy keys (e.g. <c>RENÉE</c>).</summary>
    public Villager? FindByKey(string key) => _byKey.GetValueOrDefault(key);

    /// <summary>Exact, case- and accent-insensitive match on the display name ("renee" finds "Renée").</summary>
    public Villager? FindByName(string name) => _byName.GetValueOrDefault(Fold(name));

    /// <summary>
    /// Ranked suggestions for partial or misspelled input: prefix matches, then substring matches,
    /// then the closest names by edit distance.
    /// </summary>
    public IReadOnlyList<Villager> Search(string input, int limit)
    {
        var query = Fold(input);
        if (query.Length == 0)
            return All.Take(limit).ToList();

        return All
            .Select(v => (Villager: v, Name: Fold(v.Name)))
            .Select(x => (x.Villager, Rank: x.Name.StartsWith(query, StringComparison.Ordinal) ? 0
                                          : x.Name.Contains(query, StringComparison.Ordinal) ? 1
                                          : 2,
                          Distance: EditDistance(query, x.Name)))
            .Where(x => x.Rank < 2 || x.Distance <= Math.Max(2, query.Length / 3))
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Villager.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(x => x.Villager)
            .ToList();
    }

    /// <summary>Lower-cases and strips diacritics and non-alphanumerics, so "Agent S", "agent-s" and "agents" compare equal.</summary>
    internal static string Fold(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static int EditDistance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record VillagerJson(
        string Key,
        string Name,
        string Gender,
        string Species,
        string Personality,
        string Catchphrase,
        string InternalId,
        List<string>? LegacyKeys);
}
