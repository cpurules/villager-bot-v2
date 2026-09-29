using VillagerBot.Data;

namespace VillagerBot.Bot.Staff;

public static class RequestArchive
{
    /// <summary>Moves an active request into the archive (one SaveChanges, so it's atomic).</summary>
    public static async Task ArchiveAsync(this VillagerBotDbContext db, ActiveRequest request, ArchiveOutcome outcome,
        DateTimeOffset closedAt, ulong? hunterId = null)
    {
        if (db.Entry(request).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            db.ActiveRequests.Attach(request);

        db.ActiveRequests.Remove(request);
        db.ArchivedRequests.Add(new ArchivedRequest
        {
            UserId = request.UserId,
            VillagerKey = request.VillagerKey,
            SubmittedAt = request.SubmittedAt,
            PulledAt = request.PulledAt,
            ClosedAt = closedAt,
            HunterId = hunterId ?? request.HunterId,
            Outcome = outcome,
        });
        await db.SaveChangesAsync();
    }
}
