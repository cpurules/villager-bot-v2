using NetCord;

namespace VillagerBot.Bot.Ui;

public static class Markup
{
    /// <summary>A Discord timestamp, rendered in each viewer's own time zone.</summary>
    public static string Timestamp(DateTimeOffset value, char style = 'f') => $"<t:{value.ToUnixTimeSeconds()}:{style}>";

    /// <summary>Inline custom emoji, or the fallback when the ID isn't configured (e.g. on a test server).</summary>
    public static string Emoji(ulong id, string fallback) => id == 0 ? fallback : $"<:e:{id}>";

    public static EmojiProperties EmojiProperties(ulong id, string fallback)
        => id == 0 ? NetCord.EmojiProperties.Standard(fallback) : NetCord.EmojiProperties.Custom(id);

    public static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => $"{n}th",
        _ => (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" },
    };
}
