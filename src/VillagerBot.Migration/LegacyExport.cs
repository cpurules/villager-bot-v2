using System.Globalization;
using System.Text.Json;

namespace VillagerBot.Migration;

internal sealed record LegacyRequest(
    string UserId,
    string? Rev,
    string? Villager,
    string? TimeStamp,
    string? Status,
    string? AcceptedTimeStamp,
    string? HelperUserId,
    string? ChannelId,
    bool? Available,
    long? Pos);

internal sealed record LegacyArchive(
    string? UserId,
    string? Villager,
    string? SubmissionTimeStamp,
    string? AcceptedTimeStamp,
    string? ArchiveTimeStamp,
    string? HelperUserId,
    string? Status);

/// <summary>The legacy ArangoDB collections, as exported by <c>arangoexport --type jsonl</c>.</summary>
internal sealed record LegacyExport(IReadOnlyList<LegacyRequest> Requests, IReadOnlyList<LegacyArchive> Archive)
{
    public static LegacyExport Load(string directory) => new(
        ReadLines(Path.Combine(directory, "requests.jsonl"), ParseRequest),
        ReadLines(Path.Combine(directory, "archivedrequests.jsonl"), ParseArchive));

    private static List<T> ReadLines<T>(string path, Func<JsonElement, T> parse)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Export file not found: {path}");

        return File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return parse(document.RootElement);
            })
            .ToList();
    }

    private static LegacyRequest ParseRequest(JsonElement e) => new(
        UserId: String(e, "_key") ?? "",
        Rev: String(e, "_rev"),
        Villager: String(e, "villager"),
        TimeStamp: String(e, "timeStamp"),
        Status: String(e, "status"),
        AcceptedTimeStamp: String(e, "acceptedTimeStamp"),
        HelperUserId: String(e, "helperUserId"),
        ChannelId: String(e, "channelId"),
        Available: e.TryGetProperty("available", out var a) && a.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? a.GetBoolean()
            : null,
        Pos: e.TryGetProperty("pos", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt64() : null);

    private static LegacyArchive ParseArchive(JsonElement e) => new(
        UserId: String(e, "userId"),
        Villager: String(e, "villager"),
        SubmissionTimeStamp: String(e, "submissionTimeStamp"),
        AcceptedTimeStamp: String(e, "acceptedTimeStamp"),
        ArchiveTimeStamp: String(e, "archiveTimeStamp"),
        HelperUserId: String(e, "helperUserId"),
        Status: String(e, "status"));

    private static string? String(JsonElement e, string name)
        => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>Parses the ISO-8601 timestamps the legacy bot stored (millisecond or microsecond precision, UTC).</summary>
    public static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp);
}
