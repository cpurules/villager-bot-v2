namespace VillagerBot.Core.Villagers;

/// <summary>A villager from the catalogue. <see cref="Key"/> is the stable identifier stored in the database.</summary>
public sealed record Villager(
    string Key,
    string Name,
    string Gender,
    string Species,
    string Personality,
    string Catchphrase,
    string InternalId,
    IReadOnlyList<string> LegacyKeys);
