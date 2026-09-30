namespace VillagerBot.Bot.Configuration;

/// <summary>
/// Everything server-specific, bound from the <c>VillagerBot</c> configuration section (command-design §8).
/// Discord IDs live in <c>appsettings.{Environment}.json</c> so a test server and the live server can run from the
/// same build; see <see cref="VillagerBotOptionsValidator"/> for what is required.
/// </summary>
public sealed class VillagerBotOptions
{
    public const string Section = "VillagerBot";

    public ulong GuildId { get; set; }

    public RoleOptions Roles { get; set; } = new();

    public ChannelOptions Channels { get; set; } = new();

    public CategoryOptions Categories { get; set; } = new();

    /// <summary>Custom emoji IDs. Optional: 0 falls back to a standard emoji (handy on a test server).</summary>
    public EmojiOptions Emoji { get; set; } = new();

    public PanelOptions Panel { get; set; } = new();

    /// <summary>Shown to members when a villager can't currently be requested (catalogue <c>requestable: false</c>).</summary>
    public string NotRequestableReason { get; set; } =
        "Sanrio villagers are amiibo-only and can't currently be distributed.";

    /// <summary>The bot's status line, e.g. "Playing Villager Pairing". An empty <c>Text</c> shows no activity.</summary>
    public ActivityOptions Activity { get; set; } = new();

    /// <summary>Named villager groups (by catalogue key), e.g. <c>Sanrio</c>.</summary>
    public Dictionary<string, List<string>> VillagerGroups { get; set; } = [];

    /// <summary>IANA time zone used for month and year boundaries in stats.</summary>
    public string StatsTimeZone { get; set; } = "America/Chicago";

    public JobOptions Jobs { get; set; } = new();

    public PullOptions Pull { get; set; } = new();

    public RequestChannelOptions RequestChannel { get; set; } = new();

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
        /// <summary>Holds the request panel (<c>#villager-request</c> on the live server).</summary>
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

    public sealed class PanelOptions
    {
        public string Title { get; set; } = "Villager requests";
        public string Description { get; set; } = "";
        public string VillagerListUrl { get; set; } = "https://animalcrossing.fandom.com/wiki/Villager_list_(New_Horizons)";
    }

    public sealed class ActivityOptions
    {
        /// <summary>Playing, Watching, Listening, Competing, or Custom (free-text status).</summary>
        public NetCord.Gateway.UserActivityType Type { get; set; } = NetCord.Gateway.UserActivityType.Playing;

        public string Text { get; set; } = "Villager Pairing";
    }

    public sealed class JobOptions
    {
        /// <summary>
        /// Off on the test server: its data may come from the live server, whose members aren't in the test guild and
        /// would all look like they had left.
        /// </summary>
        public bool DepartedCleanupEnabled { get; set; } = true;

        public int DepartedCleanupHours { get; set; } = 6;
    }

    public sealed class PullOptions
    {
        public int MaxCount { get; set; } = 10;

        /// <summary>Villager group left out of bulk pulls unless one of its villagers is chosen explicitly (decided).</summary>
        public string BulkExcludedGroup { get; set; } = "Sanrio";
    }

    /// <summary>
    /// Texts for request channels. Placeholders: <c>{member}</c>, <c>{hunter}</c> (mentions), <c>{villager}</c>,
    /// <c>{channel}</c> (channel mention).
    /// </summary>
    public sealed class RequestChannelOptions
    {
        public string Welcome { get; set; } =
            "Welcome {member} to your mystery island where your villager **{villager}** awaits. {hunter} will contact you " +
            "shortly and you will be allowed **30 minutes** to collect your villager. Please make sure you have a plot open " +
            "and ready, thanks.\n\n" +
            "*__REMINDER__ - if you are unresponsive (~10 minutes with no reply), your hunter may close your request!*\n" +
            "*__REMINDER__ - do not DM your hunter! Please keep all communication within this channel.*";

        public string PulledDm { get; set; } =
            "A Haven Hunter is now ready to assist you with your request for **{villager}**. In the Villager Haven Discord, " +
            "a channel is now open for your request: {channel}";

        public string TimeoutDm { get; set; } =
            "Unfortunately you were unavailable to pick up your villager, and your channel has been closed.\n" +
            "You're able to submit a new request now. In the future, please mark yourself **not available** (with `/request`) " +
            "whenever you can't pick up a villager.";
    }
}
