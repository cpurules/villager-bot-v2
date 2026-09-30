namespace VillagerBot.Core.Villagers;

/// <summary>
/// A villager from the catalogue. <see cref="Key"/> is the stable identifier stored in the database.
/// <see cref="Requestable"/> is false for villagers that can't currently be distributed (e.g. the amiibo-only Sanrio
/// villagers): members can't request them or mark such a request available.
/// </summary>
public sealed record Villager(
    string Key,
    string Name,
    string Gender,
    string Species,
    string Personality,
    string Catchphrase,
    string InternalId,
    IReadOnlyList<string> LegacyKeys,
    bool Requestable = true);
