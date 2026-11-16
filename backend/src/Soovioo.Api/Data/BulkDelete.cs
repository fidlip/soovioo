using Microsoft.EntityFrameworkCore;

namespace Soovioo.Api.Data;

public static class BulkDelete
{
    /// <summary>Deletes the rows straight in the database (no tracking) —
    /// in support mode one by one, for the journal.</summary>
    public static async Task DeleteAsync<T>(this AppDbContext db, IQueryable<T> rows) where T : class
    {
        if (db.Database.IsRelational() && db.SupportJournal is null)
        {
            await rows.ExecuteDeleteAsync();
            return;
        }
        // Also the InMemory provider (tests), which has no ExecuteDelete.
        db.RemoveRange(await rows.ToListAsync());
        await db.SaveChangesAsync();
    }
}
