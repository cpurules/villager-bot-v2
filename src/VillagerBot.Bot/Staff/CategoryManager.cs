using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using VillagerBot.Bot.Configuration;
using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

/// <summary>A problem the pulling hunter should see as-is (e.g. the main category is missing).</summary>
public sealed class RequestChannelException(string message) : Exception(message);

/// <summary>Serializes category and channel creation so simultaneous pulls can't both create an overflow category.</summary>
public sealed class ChannelCreationLock
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

/// <summary>
/// Request channels and dynamic overflow categories (command-design §6). The main category is permanent and never
/// created by the bot; overflow categories are created when every category is full and deleted when they empty out.
/// Only categories recorded in <c>overflow_categories</c> are ever deleted.
/// </summary>
public sealed class CategoryManager(
    RestClient rest,
    GatewayClient gateway,
    VillagerBotDbContext db,
    ChannelCreationLock creationLock,
    IOptions<VillagerBotOptions> options,
    TimeProvider time,
    ILogger<CategoryManager> logger)
{
    private const Permissions MemberPermissions =
        Permissions.ViewChannel | Permissions.SendMessages | Permissions.ReadMessageHistory;

    private const Permissions HunterPermissions = MemberPermissions | Permissions.ManageMessages;

    private const Permissions BotPermissions =
        MemberPermissions | Permissions.EmbedLinks | Permissions.ManageChannels;

    private readonly VillagerBotOptions _options = options.Value;

    /// <summary>Creates <c>request-&lt;villager&gt;-&lt;4 hex&gt;</c> in the first category with room.</summary>
    public async Task<ulong> CreateRequestChannelAsync(string villagerName, ulong memberId, ulong hunterId)
    {
        await creationLock.Semaphore.WaitAsync();
        try
        {
            var category = await ChooseCategoryAsync();

            var overwrites = CopyOverwrites(category);
            Grant(overwrites, gateway.Id, PermissionOverwriteType.User, BotPermissions);
            Grant(overwrites, memberId, PermissionOverwriteType.User, MemberPermissions);
            Grant(overwrites, hunterId, PermissionOverwriteType.User, HunterPermissions);

            var channel = await rest.CreateGuildChannelAsync(_options.GuildId,
                new GuildChannelProperties(ChannelName(villagerName), ChannelType.TextGuildChannel)
                {
                    ParentId = category.Id,
                    PermissionOverwrites = overwrites.Values,
                });
            return channel.Id;
        }
        finally
        {
            creationLock.Semaphore.Release();
        }
    }

    /// <summary>Deletes the category if it's a bot-created overflow category with no channels left.</summary>
    public async Task DeleteIfEmptyOverflowAsync(ulong? categoryId)
    {
        if (categoryId is not { } id || !await db.OverflowCategories.AnyAsync(c => c.CategoryId == id))
            return;

        await creationLock.Semaphore.WaitAsync();
        try
        {
            var channels = await rest.GetGuildChannelsAsync(_options.GuildId);
            if (!channels.Any(c => ParentOf(c) == id))
                await DeleteOverflowAsync(id, channels.Any(c => c.Id == id));
        }
        finally
        {
            creationLock.Semaphore.Release();
        }
    }

    /// <summary>Deletes every empty bot-created overflow category, and forgets any deleted by hand. Returns how many were removed.</summary>
    public async Task<int> CleanUpOverflowAsync()
    {
        await creationLock.Semaphore.WaitAsync();
        try
        {
            var channels = await rest.GetGuildChannelsAsync(_options.GuildId);
            var removed = 0;
            foreach (var overflow in await db.OverflowCategories.AsNoTracking().ToListAsync())
            {
                var exists = channels.Any(c => c.Id == overflow.CategoryId);
                if (!exists || !channels.Any(c => ParentOf(c) == overflow.CategoryId))
                {
                    await DeleteOverflowAsync(overflow.CategoryId, exists);
                    removed++;
                }
            }

            return removed;
        }
        finally
        {
            creationLock.Semaphore.Release();
        }
    }

    private async Task<CategoryGuildChannel> ChooseCategoryAsync()
    {
        var channels = await rest.GetGuildChannelsAsync(_options.GuildId);
        var categories = channels.OfType<CategoryGuildChannel>().ToDictionary(c => c.Id);

        if (!categories.TryGetValue(_options.Categories.Main, out var main))
        {
            throw new RequestChannelException(
                $"I can't find the main request category ({_options.Categories.Main}). It may have been deleted, or my role can't see it.");
        }

        var overflow = await db.OverflowCategories.OrderBy(c => c.Number).ToListAsync();
        var childCounts = channels.GroupBy(ParentOf).Where(g => g.Key is not null).ToDictionary(g => g.Key!.Value, g => g.Count());

        foreach (var candidate in overflow.Select(o => o.CategoryId).Prepend(main.Id))
        {
            if (categories.TryGetValue(candidate, out var category)
                && childCounts.GetValueOrDefault(candidate) < _options.Categories.MaxChannels)
            {
                return category;
            }
        }

        // Every category is full: create the next overflow category with the main category's permissions.
        var number = overflow.Count == 0 ? 2 : overflow.Max(o => o.Number) + 1;
        var overwrites = CopyOverwrites(main);
        Grant(overwrites, gateway.Id, PermissionOverwriteType.User, BotPermissions);

        var created = await rest.CreateGuildChannelAsync(_options.GuildId,
            new GuildChannelProperties(string.Format(CultureInfo.InvariantCulture, _options.Categories.OverflowNameFormat, number),
                ChannelType.CategoryChannel)
            {
                PermissionOverwrites = overwrites.Values,
            });

        db.OverflowCategories.Add(new OverflowCategory { CategoryId = created.Id, Number = number, CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync();
        logger.LogInformation("Created overflow category {Number} ({CategoryId}).", number, created.Id);

        return (CategoryGuildChannel)created;
    }

    private async Task DeleteOverflowAsync(ulong categoryId, bool existsInDiscord)
    {
        if (existsInDiscord)
            await rest.DeleteChannelAsync(categoryId);

        await db.OverflowCategories.Where(c => c.CategoryId == categoryId).ExecuteDeleteAsync();
        logger.LogInformation("Removed overflow category {CategoryId}.", categoryId);
    }

    private static Dictionary<ulong, PermissionOverwriteProperties> CopyOverwrites(IGuildChannel category)
        => category.PermissionOverwrites.Values.ToDictionary(o => o.Id,
            o => new PermissionOverwriteProperties(o.Id, o.Type) { Allowed = o.Allowed, Denied = o.Denied });

    private static void Grant(Dictionary<ulong, PermissionOverwriteProperties> overwrites, ulong id, PermissionOverwriteType type,
        Permissions permissions)
    {
        if (overwrites.TryGetValue(id, out var existing))
        {
            existing.Allowed = (existing.Allowed ?? 0) | permissions;
            existing.Denied = (existing.Denied ?? 0) & ~permissions;
        }
        else
        {
            overwrites[id] = new PermissionOverwriteProperties(id, type) { Allowed = permissions };
        }
    }

    public static ulong? ParentOf(IGuildChannel channel) => channel switch
    {
        TextGuildChannel text => text.ParentId,
        IVoiceGuildChannel voice => voice.ParentId,
        ForumGuildChannel forum => forum.ParentId,
        _ => null,
    };

    /// <summary><c>request-agent-s-3f2a</c>: lowercase villager name, letters and digits only, plus a short random suffix.</summary>
    public static string ChannelName(string villagerName)
    {
        var slug = new StringBuilder();
        foreach (var c in villagerName.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '-' || c == '.')
                slug.Append('-');
        }

        return $"request-{slug.ToString().Trim('-')}-{Random.Shared.Next(0x10000):x4}";
    }
}
