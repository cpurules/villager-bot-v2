using Microsoft.EntityFrameworkCore;
using Npgsql;
using VillagerBot.Core.Villagers;
using VillagerBot.Data;

namespace VillagerBot.Bot.Requests;

/// <summary>A member's request plus its live queue positions (docs/data-migration.md §2).</summary>
public sealed record RequestSnapshot(ActiveRequest Request, int AvailablePosition, int OverallPosition)
{
    public bool IsPulled => Request.PulledAt is not null;
}

public enum RequestChange
{
    Done,
    NotFound,

    /// <summary>A Haven Hunter has already pulled the request, so the member can no longer change it (B9).</summary>
    Pulled,

    /// <summary>The villager can't currently be requested (e.g. Sanrio), so the request can't be marked available.</summary>
    NotRequestable,
}

/// <summary>Member-side request operations. Every method re-reads current state, since buttons can be clicked late.</summary>
public sealed class RequestService(VillagerBotDbContext db, VillagerCatalog catalog, TimeProvider time)
{
    public Task<ActiveRequest?> FindAsync(ulong userId)
        => db.ActiveRequests.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == userId);

    public async Task<RequestSnapshot?> GetSnapshotAsync(ulong userId)
    {
        if (await FindAsync(userId) is not { } request)
            return null;

        var ahead = db.ActiveRequests.Where(r => r.QueuePosition < request.QueuePosition);
        var overall = await ahead.CountAsync() + 1;
        var available = await ahead.CountAsync(r => r.PulledAt == null && r.IsAvailable) + 1;
        return new RequestSnapshot(request, available, overall);
    }

    /// <summary>Creates a request at the back of the queue. Returns false if the member already has one.</summary>
    public async Task<bool> TryCreateAsync(ulong userId, string villagerKey)
    {
        if (!IsRequestable(villagerKey) || await db.ActiveRequests.AnyAsync(r => r.UserId == userId))
            return false;

        db.ActiveRequests.Add(new ActiveRequest
        {
            UserId = userId,
            VillagerKey = villagerKey,
            SubmittedAt = time.GetUtcNow(),
            IsAvailable = false,
        });

        try
        {
            await db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two confirms raced (e.g. double click); the other one won.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<RequestChange> ChangeVillagerAsync(ulong userId, string villagerKey)
        => IsRequestable(villagerKey)
            ? UpdateUnpulledAsync(userId, r => r.VillagerKey = villagerKey)
            : Task.FromResult(RequestChange.NotRequestable);

    /// <summary>Members can always go unavailable, but only mark a requestable villager's request available.</summary>
    public async Task<RequestChange> SetAvailabilityAsync(ulong userId, bool available)
    {
        if (available && await FindAsync(userId) is { } request && !IsRequestable(request.VillagerKey))
            return RequestChange.NotRequestable;

        return await UpdateUnpulledAsync(userId, r => r.IsAvailable = available);
    }

    /// <summary>
    /// Marks every waiting request for a villager that can't be requested as not available. Run at startup, so the
    /// rule also holds for requests made before a villager was blocked. Returns how many changed.
    /// </summary>
    public Task<int> EnforceNotRequestableAsync()
    {
        var blocked = catalog.All.Where(v => !v.Requestable).Select(v => v.Key).ToList();
        return db.ActiveRequests
            .Where(r => r.PulledAt == null && r.IsAvailable && blocked.Contains(r.VillagerKey))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsAvailable, false));
    }

    private bool IsRequestable(string villagerKey) => catalog.FindByKey(villagerKey)?.Requestable ?? false;

    /// <summary>Removes the request without archiving it (decided: leaving writes no history).</summary>
    public async Task<RequestChange> LeaveAsync(ulong userId)
    {
        var request = await db.ActiveRequests.FirstOrDefaultAsync(r => r.UserId == userId);
        if (request is null)
            return RequestChange.NotFound;
        if (request.PulledAt is not null)
            return RequestChange.Pulled;

        db.ActiveRequests.Remove(request);
        await db.SaveChangesAsync();
        return RequestChange.Done;
    }

    private async Task<RequestChange> UpdateUnpulledAsync(ulong userId, Action<ActiveRequest> update)
    {
        var request = await db.ActiveRequests.FirstOrDefaultAsync(r => r.UserId == userId);
        if (request is null)
            return RequestChange.NotFound;
        if (request.PulledAt is not null)
            return RequestChange.Pulled;

        update(request);
        await db.SaveChangesAsync();
        return RequestChange.Done;
    }
}
