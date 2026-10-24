using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.People;

namespace Soovioo.Api.Retention;

/// <summary>Switching accounts off and erasing the people behind them.
/// Works without a request (the daily job), so every query names its
/// school explicitly.</summary>
public class AccountEraser(AppDbContext db, RefreshTokenService refreshTokens, PhotoStorage photos, TimeProvider clock)
{
    public const string ErasedFirstName = "Vymazaný";
    public const string ErasedLastName = "uživatel";

    /// <summary>The account can't log in any more; sessions end. Caller saves.</summary>
    public async Task DeactivateAsync(User user)
    {
        if (user.DeactivatedAt is not null)
        {
            return;
        }
        user.DeactivatedAt = clock.GetUtcNow();
        user.InvitationToken = null;
        user.InvitationTokenExpiresAt = null;
        await refreshTokens.RevokeAllForUserAsync(user.Id);
    }

    public void Reactivate(User user)
    {
        if (user.ErasedAt is null)
        {
            user.DeactivatedAt = null;
        }
    }

    /// <summary>Erases the person behind the account — name, contacts,
    /// photo, choices, sign-in — and keeps an anonymous row, because events,
    /// payments and other people's conversations point at it. Saves.</summary>
    public async Task EraseAsync(User user)
    {
        if (user.ErasedAt is not null)
        {
            return;
        }
        await DeactivateAsync(user);

        var person = await db.Persons.IgnoreQueryFilters()
            .Include(p => p.TeacherProfile)
            .Include(p => p.ParentProfile)
            .FirstOrDefaultAsync(p => p.UserId == user.Id);
        string? photoKey = null;
        if (person is not null)
        {
            photoKey = person.PhotoKey;
            person.FirstName = ErasedFirstName;
            person.LastName = ErasedLastName;
            person.Phone = null;
            person.Email = null;
            person.PhotoKey = null;
            if (person.TeacherProfile is not null)
            {
                person.TeacherProfile.Qualification = null;
            }
            await db.DeleteAsync(db.VisibilityChoices.IgnoreQueryFilters().Where(c => c.PersonId == person.Id));
            if (person.ParentProfile is not null)
            {
                await db.DeleteAsync(db.ParentStudents.IgnoreQueryFilters().Where(ps => ps.ParentProfileId == person.Id));
            }
        }

        user.Username = $"vymazany-{user.Id}";
        // Not a hash any password can match.
        user.PasswordHash = "";
        user.MustChangePassword = false;
        user.CalendarToken = null;
        user.ErasedAt = clock.GetUtcNow();
        await db.SaveChangesAsync();
        photos.Delete(photoKey);
    }
}
