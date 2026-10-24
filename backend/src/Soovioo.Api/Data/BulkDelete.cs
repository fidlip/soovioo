using Microsoft.EntityFrameworkCore;

namespace Soovioo.Api.Data;

public static class BulkDelete
{
    /// <summary>Deletes the rows straight in the database (no tracking).</summary>
    public static async Task DeleteAsync<T>(this AppDbContext db, IQueryable<T> rows) where T : class
    {
        if (db.Database.IsRelational())
        {
            await rows.ExecuteDeleteAsync();
            return;
        }
        // The InMemory provider (tests) has no ExecuteDelete.
        db.RemoveRange(await rows.ToListAsync());
        await db.SaveChangesAsync();
    }
}
