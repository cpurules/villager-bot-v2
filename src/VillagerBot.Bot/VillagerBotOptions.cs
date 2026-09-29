namespace VillagerBot.Bot;

/// <summary>Everything server-specific, bound from the <c>VillagerBot</c> configuration section (command-design §8).</summary>
public sealed class VillagerBotOptions
{
    public const string Section = "VillagerBot";

    public ulong GuildId { get; set; }

    public RoleOptions Roles { get; set; } = new();

    public ChannelOptions Channels { get; set; } = new();

    public CategoryOptions Categories { get; set; } = new();

    public EmojiOptions Emoji { get; set; } = new();

    /// <summary>Named villager groups (by catalogue key), e.g. <c>Sanrio</c>.</summary>
    public Dictionary<string, List<string>> VillagerGroups { get; set; } = [];

    /// <summary>IANA time zone used for month and year boundaries in stats.</summary>
    public string StatsTimeZone { get; set; } = "America/Chicago";

    public JobOptions Jobs { get; set; } = new();

    public PullOptions Pull { get; set; } = new();

    public sealed class RoleOptions
    {
        public ulong Villagers { get; set; }
        public ulong HavenHunter { get; set; }
        public ulong Moderator { get; set; }
        public ulong Haven { get; set; }
        public ulong HunterHiatus { get; set; }
    }

    public sealed class ChannelOptions
    {
        public ulong Info { get; set; }
        public ulong Requests { get; set; }
        public ulong ModMail { get; set; }
        public ulong StarterKits { get; set; }
        public ulong StaffLog { get; set; }
        public ulong TimeoutLog { get; set; }
    }

    public sealed class CategoryOptions
    {
        public ulong Main { get; set; }
        public string OverflowNameFormat { get; set; } = "Villager Requests {0}";
        public int MaxChannels { get; set; } = 50;
    }

    public sealed class EmojiOptions
    {
        public ulong Available { get; set; }
        public ulong Unavailable { get; set; }
        public ulong ModMail { get; set; }
        public ulong StarterKit { get; set; }
    }

    public sealed class JobOptions
    {
        public int DepartedCleanupHours { get; set; } = 6;
    }

    public sealed class PullOptions
    {
        public int MaxCount { get; set; } = 10;
    }
}
