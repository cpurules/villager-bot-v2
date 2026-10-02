using VillagerBot.Core.Villagers;
using VillagerBot.Data;
using VillagerBot.Migration;

namespace VillagerBot.Tests;

public class ImportTests
{
    private static readonly VillagerCatalog Catalog = VillagerCatalog.LoadEmbedded();
    private static readonly DateTimeOffset Now = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset StaleBefore = Now.AddDays(-365);

    [Fact]
    public void DecodesArangoRevisionTimestamps()
    {
        var expected = new DateTimeOffset(2026, 3, 1, 12, 30, 45, 123, TimeSpan.Zero);
        Assert.True(ArangoRevision.TryDecodeTimestamp(Rev(expected), out var decoded));
        Assert.Equal(expected, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a rev!")]
    public void RejectsInvalidRevisions(string? rev) => Assert.False(ArangoRevision.TryDecodeTimestamp(rev, out _));

    [Fact]
    public void ArchivesStaleRequestsAndKeepsRecentOnes()
    {
        var plan = Plan(
            Request("1", "RAYMOND", submitted: Now.AddDays(-800), lastWrite: Now.AddDays(-400)),
            Request("2", "AUDIE", submitted: Now.AddDays(-800), lastWrite: Now.AddDays(-10)));

        var kept = Assert.Single(plan.Active);
        Assert.Equal(2UL, kept.UserId);
        var archived = Assert.Single(plan.Archived);
        Assert.Equal((1UL, ArchiveOutcome.Removed, Now), (archived.UserId, archived.Outcome, archived.ClosedAt));
        Assert.Equal(1, plan.Report.ArchivedAsStale);
    }

    [Fact]
    public void ArchivesLegacyPullsAsRemoved()
    {
        var plan = Plan(Request("1", "RAYMOND", submitted: Now.AddDays(-5), lastWrite: Now.AddDays(-1), status: "ACCEPTED",
            accepted: Now.AddDays(-2), helper: "99"));

        Assert.Empty(plan.Active);
        var archived = Assert.Single(plan.Archived);
        Assert.Equal(ArchiveOutcome.Removed, archived.Outcome);
        Assert.Equal(99UL, archived.HunterId);
        Assert.Equal(Now.AddDays(-2), archived.PulledAt);
    }

    [Fact]
    public void RenumbersKeptRequestsBySubmissionTime()
    {
        var plan = Plan(
            Request("1", "RAYMOND", submitted: Now.AddDays(-3), lastWrite: Now, pos: 900),
            Request("2", "AUDIE", submitted: Now.AddDays(-9), lastWrite: Now, pos: 950),
            Request("3", "JUDY", submitted: Now.AddDays(-9), lastWrite: Now, pos: 10));

        Assert.Equal([3UL, 2UL, 1UL], plan.Active.OrderBy(r => r.QueuePosition).Select(r => r.UserId));
        Assert.Equal([1L, 2L, 3L], plan.Active.Select(r => r.QueuePosition).Order());
    }

    [Fact]
    public void MapsLegacyVillagerKeysAndAvailability()
    {
        var plan = Plan(Request("1", "RENÉE", submitted: Now.AddDays(-1), lastWrite: Now, available: true));

        var kept = Assert.Single(plan.Active);
        Assert.Equal(("RENEE", true), (kept.VillagerKey, kept.IsAvailable));
    }

    [Fact]
    public void ReportsUnknownVillagersWithoutImportingThem()
    {
        var plan = Plan(Request("1", "NOT_A_VILLAGER", submitted: Now, lastWrite: Now));

        Assert.Empty(plan.Active);
        Assert.Single(plan.Report.Problems);
    }

    [Fact]
    public void ImportsArchiveRows()
    {
        var archive = new LegacyArchive("5", "RAYMOND", Iso(Now.AddDays(-3)), null, Iso(Now.AddDays(-2)), null, "TIMEOUT");
        var plan = ImportPlanner.Plan(new LegacyExport([], [archive]), Catalog, Now, StaleBefore);

        var row = Assert.Single(plan.Archived);
        Assert.Equal((5UL, ArchiveOutcome.Timeout, (DateTimeOffset?)null, (ulong?)null),
            (row.UserId, row.Outcome, row.PulledAt, row.HunterId));
    }

    private static ImportPlan Plan(params LegacyRequest[] requests)
        => ImportPlanner.Plan(new LegacyExport(requests, []), Catalog, Now, StaleBefore);

    private static LegacyRequest Request(string userId, string villager, DateTimeOffset submitted, DateTimeOffset lastWrite,
        string status = "UNACCEPTED", DateTimeOffset? accepted = null, string? helper = null, bool available = false, long pos = 1)
        => new(userId, Rev(lastWrite), villager, Iso(submitted), status, accepted is { } a ? Iso(a) : null, helper, null,
            available, pos);

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("O");

    /// <summary>Encodes a timestamp the way ArangoDB does (HLC: milliseconds &lt;&lt; 20, custom base64).</summary>
    private static string Rev(DateTimeOffset timestamp)
    {
        const string alphabet = "-_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var value = (ulong)timestamp.ToUnixTimeMilliseconds() << 20;
        var chars = new Stack<char>();
        while (value > 0)
        {
            chars.Push(alphabet[(int)(value & 63)]);
            value >>= 6;
        }

        return new string(chars.ToArray());
    }
}
