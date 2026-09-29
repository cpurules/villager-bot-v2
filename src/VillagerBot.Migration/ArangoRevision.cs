namespace VillagerBot.Migration;

/// <summary>
/// Decodes ArangoDB <c>_rev</c> values. Since ArangoDB 3.x a revision is a hybrid-logical-clock stamp written in a
/// custom base64 alphabet; the upper bits (<c>value &gt;&gt; 20</c>) are Unix milliseconds of the document's last write.
/// </summary>
internal static class ArangoRevision
{
    private const string Alphabet = "-_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static bool TryDecodeTimestamp(string? rev, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrEmpty(rev) || rev.Length > 11)
            return false;

        ulong value = 0;
        foreach (var c in rev)
        {
            var digit = Alphabet.IndexOf(c);
            if (digit < 0)
                return false;
            value = (value << 6) | (uint)digit;
        }

        var milliseconds = (long)(value >> 20);
        if (milliseconds <= 0 || milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            return false;

        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        return true;
    }
}
