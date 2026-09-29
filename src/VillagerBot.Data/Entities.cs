namespace VillagerBot.Data;

/// <summary>A member's open villager request. One per member.</summary>
public class ActiveRequest
{
    public ulong UserId { get; set; }

    public required string VillagerKey { get; set; }

    /// <summary>
    /// Static FIFO "ticket" from the <c>queue_position</c> sequence. Never changes after creation; displayed positions
    /// are computed from it (see docs/data-migration.md §2).
    /// </summary>
    public long QueuePosition { get; set; }

    public DateTimeOffset SubmittedAt { get; set; }

    public bool IsAvailable { get; set; }

    /// <summary>When a Haven Hunter pulled the request; null while it's waiting in the queue.</summary>
    public DateTimeOffset? PulledAt { get; set; }

    public ulong? HunterId { get; set; }

    public ulong? ChannelId { get; set; }
}

/// <summary>A closed request, kept for history and stats.</summary>
public class ArchivedRequest
{
    public long Id { get; set; }

    public ulong UserId { get; set; }

    public required string VillagerKey { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? PulledAt { get; set; }

    public DateTimeOffset ClosedAt { get; set; }

    public ulong? HunterId { get; set; }

    public ArchiveOutcome Outcome { get; set; }
}

public enum ArchiveOutcome
{
    Completed,
    Timeout,

    /// <summary>Removed without being fulfilled: the member left the server, or the request expired as stale.</summary>
    Removed,
}

/// <summary>An overflow request category the bot created, and may therefore delete.</summary>
public class OverflowCategory
{
    public ulong CategoryId { get; set; }

    public int Number { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Small key/value settings, e.g. the panel message location.</summary>
public class BotStateEntry
{
    public required string Key { get; set; }

    public required string Value { get; set; }
}
