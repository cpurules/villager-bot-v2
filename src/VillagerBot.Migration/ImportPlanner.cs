using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Migration;

internal sealed class ImportReport
{
    public int RequestsRead { get; set; }
    public int ArchiveRowsRead { get; set; }
    public int KeptActive { get; set; }
    public int KeptAvailable { get; set; }
    public int ArchivedAsStale { get; set; }
    public int ArchivedAsStalePull { get; set; }
    public int RevFallbacks { get; set; }

    /// <summary>Kept requests marked available in the legacy data but imported as not available (villager not requestable).</summary>
    public int MadeUnavailable { get; set; }
    public Dictionary<ArchiveOutcome, int> ArchiveByOutcome { get; } = [];

    /// <summary>Rows that could not be imported. <c>--apply</c> refuses to run while any exist unless forced.</summary>
    public List<string> Problems { get; } = [];
}

internal sealed record ImportPlan(
    IReadOnlyList<ActiveRequest> Active,
    IReadOnlyList<ArchivedRequest> Archived,
    ImportReport Report);

/// <summary>
/// Turns the legacy export into rows for the new schema. Rules (docs/data-migration.md §4):
/// <list type="bullet">
/// <item>Requests the legacy bot marked <c>ACCEPTED</c> are stale pulls and are archived as <see cref="ArchiveOutcome.Removed"/>.</item>
/// <item>Requests whose last write (decoded from <c>_rev</c>) is before <c>staleBefore</c> are archived as Removed.</item>
/// <item>Kept requests are renumbered 1…N by submission time, ties broken by legacy <c>pos</c>.</item>
/// <item>Kept requests for villagers that can't currently be requested (e.g. Sanrio) are imported as not available.</item>
/// </list>
/// </summary>
internal static class ImportPlanner
{
    public static ImportPlan Plan(LegacyExport export, VillagerCatalog catalog, DateTimeOffset now, DateTimeOffset staleBefore)
    {
        var report = new ImportReport
        {
            RequestsRead = export.Requests.Count,
            ArchiveRowsRead = export.Archive.Count,
        };

        var archived = new List<ArchivedRequest>();
        var kept = new List<(ActiveRequest Request, long LegacyPos)>();

        foreach (var legacy in export.Requests)
        {
            var context = $"request {legacy.UserId}";
            if (!ulong.TryParse(legacy.UserId, out var userId))
            {
                report.Problems.Add($"{context}: user ID is not a number");
                continue;
            }

            if (ResolveVillager(catalog, legacy.Villager) is not { } villager)
            {
                report.Problems.Add($"{context}: unknown villager '{legacy.Villager}'");
                continue;
            }

            if (!LegacyExport.TryParseTimestamp(legacy.TimeStamp, out var submittedAt))
            {
                report.Problems.Add($"{context}: missing or invalid timeStamp '{legacy.TimeStamp}'");
                continue;
            }

            if (legacy.Status == "ACCEPTED")
            {
                archived.Add(new ArchivedRequest
                {
                    UserId = userId,
                    VillagerKey = villager.Key,
                    SubmittedAt = submittedAt,
                    PulledAt = LegacyExport.TryParseTimestamp(legacy.AcceptedTimeStamp, out var pulledAt) ? pulledAt : null,
                    ClosedAt = now,
                    HunterId = ParseId(legacy.HelperUserId),
                    Outcome = ArchiveOutcome.Removed,
                });
                report.ArchivedAsStalePull++;
                continue;
            }

            if (legacy.Status is not ("UNACCEPTED" or null))
            {
                report.Problems.Add($"{context}: unexpected status '{legacy.Status}'");
                continue;
            }

            if (!ArangoRevision.TryDecodeTimestamp(legacy.Rev, out var lastWrite))
            {
                lastWrite = submittedAt;
                report.RevFallbacks++;
            }

            if (lastWrite < staleBefore)
            {
                archived.Add(new ArchivedRequest
                {
                    UserId = userId,
                    VillagerKey = villager.Key,
                    SubmittedAt = submittedAt,
                    ClosedAt = now,
                    Outcome = ArchiveOutcome.Removed,
                });
                report.ArchivedAsStale++;
                continue;
            }

            kept.Add((new ActiveRequest
            {
                UserId = userId,
                VillagerKey = villager.Key,
                SubmittedAt = submittedAt,
                IsAvailable = (legacy.Available ?? false) && villager.Requestable,
            }, legacy.Pos ?? long.MaxValue));
            if (legacy.Available == true && !villager.Requestable)
                report.MadeUnavailable++;
        }

        var active = kept
            .OrderBy(k => k.Request.SubmittedAt)
            .ThenBy(k => k.LegacyPos)
            .Select((k, index) =>
            {
                k.Request.QueuePosition = index + 1;
                return k.Request;
            })
            .ToList();
        report.KeptActive = active.Count;
        report.KeptAvailable = active.Count(r => r.IsAvailable);

        foreach (var legacy in export.Archive)
        {
            var context = $"archive row for {legacy.UserId}";
            if (!ulong.TryParse(legacy.UserId, out var userId))
            {
                report.Problems.Add($"{context}: user ID is not a number");
                continue;
            }

            if (ResolveVillager(catalog, legacy.Villager) is not { } villager)
            {
                report.Problems.Add($"{context}: unknown villager '{legacy.Villager}'");
                continue;
            }

            if (!LegacyExport.TryParseTimestamp(legacy.ArchiveTimeStamp, out var closedAt))
            {
                report.Problems.Add($"{context}: missing or invalid archiveTimeStamp '{legacy.ArchiveTimeStamp}'");
                continue;
            }

            ArchiveOutcome? outcome = legacy.Status switch
            {
                "COMPLETED" => ArchiveOutcome.Completed,
                "TIMEOUT" => ArchiveOutcome.Timeout,
                "REMOVED" => ArchiveOutcome.Removed,
                _ => null,
            };
            if (outcome is null)
            {
                report.Problems.Add($"{context}: unexpected status '{legacy.Status}'");
                continue;
            }

            archived.Add(new ArchivedRequest
            {
                UserId = userId,
                VillagerKey = villager.Key,
                SubmittedAt = LegacyExport.TryParseTimestamp(legacy.SubmissionTimeStamp, out var submittedAt) ? submittedAt : null,
                PulledAt = LegacyExport.TryParseTimestamp(legacy.AcceptedTimeStamp, out var pulledAt) ? pulledAt : null,
                ClosedAt = closedAt,
                HunterId = ParseId(legacy.HelperUserId),
                Outcome = outcome.Value,
            });
        }

        foreach (var group in archived.GroupBy(a => a.Outcome))
            report.ArchiveByOutcome[group.Key] = group.Count();

        return new ImportPlan(active, archived, report);
    }

    private static Villager? ResolveVillager(VillagerCatalog catalog, string? key)
        => key is null ? null : catalog.FindByKey(key);

    private static ulong? ParseId(string? value) => ulong.TryParse(value, out var id) ? id : null;
}
