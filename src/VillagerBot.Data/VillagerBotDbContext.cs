using Microsoft.EntityFrameworkCore;

namespace VillagerBot.Data;

public class VillagerBotDbContext(DbContextOptions<VillagerBotDbContext> options) : DbContext(options)
{
    public const string QueuePositionSequence = "queue_position";

    public DbSet<ActiveRequest> ActiveRequests => Set<ActiveRequest>();

    public DbSet<ArchivedRequest> ArchivedRequests => Set<ArchivedRequest>();

    public DbSet<OverflowCategory> OverflowCategories => Set<OverflowCategory>();

    public DbSet<BotStateEntry> BotState => Set<BotStateEntry>();

    /// <summary>Applies the provider settings shared by the bot, the migration tool, and design-time tooling.</summary>
    public static void Configure(DbContextOptionsBuilder builder, string connectionString)
        => builder.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Discord snowflakes fit in a signed 64-bit integer; Postgres has no unsigned bigint.
        configurationBuilder.Properties<ulong>().HaveConversion<long>();
        configurationBuilder.Properties<ulong?>().HaveConversion<long?>();
        configurationBuilder.Properties<ArchiveOutcome>().HaveConversion<string>().HaveMaxLength(16);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasSequence<long>(QueuePositionSequence);

        modelBuilder.Entity<ActiveRequest>(e =>
        {
            e.HasKey(r => r.UserId);
            e.Property(r => r.UserId).ValueGeneratedNever();
            e.Property(r => r.VillagerKey).HasMaxLength(32);
            e.Property(r => r.QueuePosition).HasDefaultValueSql($"nextval('{QueuePositionSequence}')");
            e.HasIndex(r => r.QueuePosition).IsUnique();
            e.HasIndex(r => new { r.PulledAt, r.IsAvailable, r.QueuePosition });
            e.HasIndex(r => r.VillagerKey);
            e.HasIndex(r => r.ChannelId).IsUnique();
        });

        modelBuilder.Entity<ArchivedRequest>(e =>
        {
            e.Property(r => r.VillagerKey).HasMaxLength(32);
            e.HasIndex(r => new { r.UserId, r.ClosedAt });
            e.HasIndex(r => new { r.Outcome, r.ClosedAt });
            e.HasIndex(r => r.VillagerKey);
        });

        modelBuilder.Entity<OverflowCategory>(e =>
        {
            e.HasKey(c => c.CategoryId);
            e.Property(c => c.CategoryId).ValueGeneratedNever();
            e.HasIndex(c => c.Number).IsUnique();
        });

        modelBuilder.Entity<BotStateEntry>(e =>
        {
            e.ToTable("bot_state");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(64);
        });
    }
}
