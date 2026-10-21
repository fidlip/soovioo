using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Demo;

public enum DemoCreateError
{
    None,
    Invalid,
    TooManyActive,
}

public record DemoCreated(int SchoolId, string Slug, DateTimeOffset ExpiresAt, DemoFeaturedUsers Featured);

/// <summary>Creating, resetting and deleting "Vyzkoušet" demo schools, and
/// the e-mails to their visitors.</summary>
public class DemoSchoolService(
    AppDbContext db,
    DemoDataGenerator generator,
    DemoSchoolPurger purger,
    [FromKeyedServices(SmtpEmailSender.Key)] IEmailSender smtp,
    IOptions<AppLinkOptions> links,
    IOptions<DemoOptions> options,
    TimeProvider time,
    ILogger<DemoSchoolService> logger)
{
    public const int MaxNameLength = 100;

    public static string? Validate(DemoSetupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
        {
            return "name";
        }
        if (request.ClassesPerGrade is < 1 or > 3)
        {
            return "classesPerGrade";
        }
        if (request.Email is { Length: > 0 } email && !email.Contains('@'))
        {
            return "email";
        }
        return null;
    }

    public async Task<(DemoCreated? Demo, DemoCreateError Error)> CreateAsync(DemoSetupRequest request)
    {
        if (Validate(request) is not null)
        {
            return (null, DemoCreateError.Invalid);
        }
        if (await db.Schools.IgnoreQueryFilters().CountAsync(s => s.IsDemo) >= options.Value.MaxActive)
        {
            return (null, DemoCreateError.TooManyActive);
        }

        var now = time.GetUtcNow();
        string slug;
        do
        {
            slug = DemoSlugs.NewSlug();
        }
        while (await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Slug == slug));

        var school = new School
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Type = request.Type,
            IsDemo = true,
            CreatedAt = now,
            ExpiresAt = now.AddDays(options.Value.LifetimeDays),
        };
        db.Schools.Add(school);
        await db.SaveChangesAsync();

        DemoFeaturedUsers featured;
        try
        {
            featured = await GenerateAsync(school, request, now);
        }
        catch
        {
            // No half-generated demo left behind.
            await purger.PurgeAsync(school.Id, deleteSchool: true);
            throw;
        }

        if (request.Email is { Length: > 0 } email)
        {
            db.DemoContacts.Add(new DemoContact
            {
                SchoolId = school.Id,
                DemoSlug = slug,
                Email = email.Trim(),
                Consent = request.Consent,
                Source = "wizard",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
            await TrySendAsync(email.Trim(), $"Vaše demo Soovioo — {school.Name}", LinkEmail(school));
        }

        return (new DemoCreated(school.Id, slug, school.ExpiresAt!.Value, featured), DemoCreateError.None);
    }

    /// <summary>Back to freshly generated data (with the wizard's new
    /// answers); the deletion date stays — a reset isn't a way to keep a
    /// demo forever.</summary>
    public async Task<DemoCreated?> ResetAsync(School school, DemoSetupRequest request)
    {
        if (!school.IsDemo || Validate(request) is not null)
        {
            return null;
        }
        await purger.PurgeAsync(school.Id, deleteSchool: false);
        var fresh = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == school.Id);
        fresh.Name = request.Name.Trim();
        fresh.Type = request.Type;
        await db.SaveChangesAsync();
        var featured = await GenerateAsync(fresh, request, time.GetUtcNow());
        return new DemoCreated(fresh.Id, fresh.Slug, fresh.ExpiresAt!.Value, featured);
    }

    /// <summary>"Vymazat data": an empty school to set up from scratch —
    /// everything goes except the log and one admin account to log in
    /// with (the featured one, else any admin). Returns that admin's id,
    /// null when it isn't a demo or has no admin left.</summary>
    public async Task<int?> ClearAsync(School school)
    {
        if (!school.IsDemo)
        {
            return null;
        }
        var adminIds = await db.Users.IgnoreQueryFilters()
            .Where(u => u.SchoolId == school.Id && u.Roles.Contains(UserRole.Admin))
            .Select(u => u.Id)
            .ToListAsync();
        int? adminId = school.DemoFeaturedUserIds?.FirstOrDefault(adminIds.Contains) is int featured && featured != 0
            ? featured
            : adminIds.Count > 0 ? adminIds.Min() : null;
        if (adminId is null)
        {
            return null;
        }

        await purger.PurgeAsync(school.Id, deleteSchool: false, keepAuditLog: true, keepUserId: adminId);
        var fresh = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == school.Id);
        fresh.DemoFeaturedUserIds = [adminId.Value];
        await db.SaveChangesAsync();
        return adminId;
    }

    private async Task<DemoFeaturedUsers> GenerateAsync(School school, DemoSetupRequest request, DateTimeOffset now)
    {
        // Change tracking of tens of thousands of new rows is the slow part.
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            var featured = await generator.GenerateAsync(
                school, new DemoSchoolOptions(request.Type, request.ClassesPerGrade, RandomNumberGenerator.GetInt32(int.MaxValue)), now);
            school.DemoFeaturedUserIds = [featured.AdminId, featured.TeacherId, featured.ParentId, featured.StudentId];
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
            return featured;
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Daily: deletes expired demos and reminds their visitors 7
    /// days and 1 day before.</summary>
    public async Task RunDailyAsync()
    {
        var now = time.GetUtcNow();
        var expired = await db.Schools.IgnoreQueryFilters()
            .Where(s => s.IsDemo && s.ExpiresAt <= now)
            .Select(s => s.Id)
            .ToListAsync();
        foreach (var id in expired)
        {
            await purger.PurgeAsync(id, deleteSchool: true);
        }

        var contacts = await db.DemoContacts
            .Where(c => c.SchoolId != null && (!c.Warned7Days || !c.Warned1Day))
            .ToListAsync();
        var schools = await db.Schools.IgnoreQueryFilters()
            .Where(s => s.IsDemo)
            .ToDictionaryAsync(s => s.Id);
        foreach (var contact in contacts)
        {
            if (!schools.TryGetValue(contact.SchoolId!.Value, out var school) || school.ExpiresAt is not { } expires)
            {
                continue;
            }
            var left = expires - now;
            if (left <= TimeSpan.FromDays(1) && !contact.Warned1Day)
            {
                contact.Warned1Day = contact.Warned7Days = true;
                await TrySendAsync(contact.Email, $"Vaše demo Soovioo bude zítra smazáno — {school.Name}", ExpiryEmail(school, "zítra"));
            }
            else if (left <= TimeSpan.FromDays(7) && !contact.Warned7Days)
            {
                contact.Warned7Days = true;
                await TrySendAsync(contact.Email, $"Vaše demo Soovioo bude za týden smazáno — {school.Name}", ExpiryEmail(school, "za týden"));
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Stores the feedback, keeps a contact left in it (like the
    /// wizard's), and forwards it to the team.</summary>
    public async Task SaveFeedbackAsync(School school, DemoFeedback feedback)
    {
        feedback.CreatedAt = time.GetUtcNow();
        db.DemoFeedback.Add(feedback);
        if (feedback.Email is { } email
            && !await db.DemoContacts.AnyAsync(c => c.SchoolId == school.Id && c.Email == email))
        {
            db.DemoContacts.Add(new DemoContact
            {
                SchoolId = school.Id,
                DemoSlug = school.Slug,
                Email = email,
                Consent = feedback.Consent,
                Source = "feedback",
                CreatedAt = feedback.CreatedAt,
            });
        }
        await db.SaveChangesAsync();

        var stars = feedback.Rating is { } r ? new string('★', r) + new string('☆', 5 - r) : "bez hodnocení";
        await TrySendAsync(options.Value.FeedbackEmail, $"Zpětná vazba z dema ({stars})", $"""
            <p><strong>Demo:</strong> {Html(school.Name)} ({school.Slug}, {school.Type})<br>
            <strong>Role:</strong> {Html(feedback.Role ?? "?")}<br>
            <strong>Hodnocení:</strong> {stars}<br>
            <strong>E-mail:</strong> {Html(feedback.Email ?? "—")} (souhlas s kontaktem: {(feedback.Consent ? "ano" : "ne")})</p>
            <p style="white-space: pre-wrap">{Html(feedback.Text ?? "")}</p>
            """);
    }

    private string DemoLink(School school) =>
        $"{links.Value.SchoolOrigin(school.Slug)}/login";

    private string LinkEmail(School school)
    {
        var link = DemoLink(school);
        return $"""
            <p>Dobrý den,</p>
            <p>děkujeme, že zkoušíte Soovioo. Vaše ukázková škola <strong>{Html(school.Name)}</strong>
            je připravená na adrese:</p>
            <p><a href="{link}">{link}</a></p>
            <p>Odkaz otevře demo i na jiném zařízení. Demo bude i se všemi daty smazáno
            {school.ExpiresAt!.Value.ToOffset(TimeSpan.FromHours(1)):d. M. yyyy}; týden a den předem vám připomeneme.</p>
            <p>Dotazy a připomínky rádi uvítáme na <a href="mailto:info@soovioo.cz">info@soovioo.cz</a>.</p>
            """;
    }

    private string ExpiryEmail(School school, string when)
    {
        var link = DemoLink(school);
        return $"""
            <p>Dobrý den,</p>
            <p>vaše ukázková škola <strong>{Html(school.Name)}</strong> v Soovioo bude {when}
            smazána i se všemi daty. Ještě ji můžete projít na adrese <a href="{link}">{link}</a>.</p>
            <p>Pokud vás Soovioo zaujalo, napište nám na <a href="mailto:info@soovioo.cz">info@soovioo.cz</a>
            — rádi vám pomůžeme s ostrým provozem.</p>
            """;
    }

    /// <summary>Escapes markup only — Czech letters stay readable in the
    /// source (WebUtility.HtmlEncode turns "á" into "&amp;#225;").</summary>
    private static string Html(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private async Task TrySendAsync(string to, string subject, string body)
    {
        try
        {
            await smtp.SendAsync(to, subject, body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Demo e-mail to a visitor wasn't sent.");
        }
    }
}

/// <param name="ClassesPerGrade">Parallel classes per grade, 1–3.</param>
/// <param name="Email">Optional — the demo's link and expiry reminders go there.</param>
/// <param name="Consent">May we contact the visitor about Soovioo (kept after the demo).</param>
public record DemoSetupRequest(
    SchoolType Type, string Name, int ClassesPerGrade = 2, string? Email = null, bool Consent = false);
