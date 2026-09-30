using System.Globalization;
using Microsoft.EntityFrameworkCore;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;
using VillagerBot.Migration;

// Imports the legacy ArangoDB export into the new Postgres schema. Dry run by default; see docs/data-migration.md §5.
//
//   --export <dir>      directory holding requests.jsonl and archivedrequests.jsonl (default: data-export)
//   --stale-days <n>    archive requests not written to in the last n days (default: 365)
//   --as-of <iso>       treat this instant as "now" (default: current time)
//   --apply             write to the database in ConnectionStrings__VillagerBot (must be empty)
//   --force             apply even if the report lists problem rows (they are skipped)

var options = ParseArgs(args);
var exportDirectory = options.GetValueOrDefault("export", "data-export");
var staleDays = int.Parse(options.GetValueOrDefault("stale-days", "365"), CultureInfo.InvariantCulture);
var now = options.TryGetValue("as-of", out var asOf)
    ? DateTimeOffset.Parse(asOf, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
    : DateTimeOffset.UtcNow;
var staleBefore = now.AddDays(-staleDays);

var export = LegacyExport.Load(exportDirectory);
var plan = ImportPlanner.Plan(export, VillagerCatalog.LoadEmbedded(), now, staleBefore);
PrintReport(plan.Report, now, staleBefore);

if (!options.ContainsKey("apply"))
{
    Console.WriteLine();
    Console.WriteLine("Dry run: nothing written. Re-run with --apply to import.");
    return 0;
}

if (plan.Report.Problems.Count > 0 && !options.ContainsKey("force"))
{
    Console.Error.WriteLine("Refusing to apply: the report lists problem rows. Fix them or re-run with --force to skip them.");
    return 1;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__VillagerBot");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__VillagerBot to the target database.");
    return 1;
}

var dbOptions = new DbContextOptionsBuilder<VillagerBotDbContext>();
VillagerBotDbContext.Configure(dbOptions, connectionString);
await using var db = new VillagerBotDbContext(dbOptions.Options);

Console.WriteLine();
Console.WriteLine("Applying schema migrations...");
await db.Database.MigrateAsync();

if (await db.ActiveRequests.AnyAsync() || await db.ArchivedRequests.AnyAsync())
{
    Console.Error.WriteLine("Refusing to apply: the database already contains requests. Import only into an empty database.");
    return 1;
}

await using var transaction = await db.Database.BeginTransactionAsync();
db.ActiveRequests.AddRange(plan.Active);
db.ArchivedRequests.AddRange(plan.Archived);
await db.SaveChangesAsync();

// Continue ticket numbers after the renumbered queue.
var lastPosition = plan.Active.Count;
await db.Database.ExecuteSqlRawAsync(lastPosition > 0
    ? $"SELECT setval('{VillagerBotDbContext.QueuePositionSequence}', {lastPosition}, true)"
    : $"SELECT setval('{VillagerBotDbContext.QueuePositionSequence}', 1, false)");

await transaction.CommitAsync();
Console.WriteLine($"Imported {plan.Active.Count} active and {plan.Archived.Count} archived requests.");
return 0;

static Dictionary<string, string> ParseArgs(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Unexpected argument '{args[i]}'.");

        var name = args[i][2..];
        var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        result[name] = hasValue ? args[++i] : "true";
    }

    return result;
}

static void PrintReport(ImportReport report, DateTimeOffset now, DateTimeOffset staleBefore)
{
    Console.WriteLine($"Import as of {now:u}; requests last written before {staleBefore:u} are archived as stale.");
    Console.WriteLine();
    Console.WriteLine($"Legacy requests read:        {report.RequestsRead,6}");
    Console.WriteLine($"  kept in the queue:         {report.KeptActive,6}  ({report.KeptAvailable} available)");
    Console.WriteLine($"  archived as stale:         {report.ArchivedAsStale,6}");
    Console.WriteLine($"  archived as stale pulls:   {report.ArchivedAsStalePull,6}");
    Console.WriteLine($"  set not available (villager can't be requested): {report.MadeUnavailable}");
    Console.WriteLine($"  _rev undecodable (used timeStamp): {report.RevFallbacks}");
    Console.WriteLine($"Legacy archive rows read:    {report.ArchiveRowsRead,6}");
    Console.WriteLine("Archive after import, by outcome:");
    foreach (var (outcome, count) in report.ArchiveByOutcome.OrderBy(p => p.Key))
        Console.WriteLine($"  {outcome,-10} {count,6}");

    Console.WriteLine();
    Console.WriteLine(report.Problems.Count == 0 ? "Problems: none" : $"Problems: {report.Problems.Count}");
    foreach (var problem in report.Problems.Take(50))
        Console.WriteLine($"  - {problem}");
    if (report.Problems.Count > 50)
        Console.WriteLine($"  ... and {report.Problems.Count - 50} more");
}
