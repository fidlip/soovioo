using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;

namespace Soovioo.Api.Tests.Data;

/// <summary>
/// The per-school EF Core query filters in AppDbContext.OnModelCreating are the
/// one thing standing between "Admin of school A" and "sees school B's data" —
/// see the multi-tenancy design notes in ROADMAP.md. These tests exist so a
/// future change to that filter logic (or to which entities have one) fails
/// loudly instead of silently leaking data cross-tenant.
/// </summary>
public class MultiTenancyQueryFilterTests
{
    private static (DbContextOptions<AppDbContext> options, int schoolAId, int schoolBId) SeedTwoSchools()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // Seeding runs with a SuperAdmin context so both schools' rows are
        // actually written (a non-SuperAdmin context would silently only
        // ever see its own school via the filter while reading back).
        using var seedDb = new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: true));
        var schoolA = new School { Name = "Škola A", Slug = "a" };
        var schoolB = new School { Name = "Škola B", Slug = "b" };
        seedDb.Schools.AddRange(schoolA, schoolB);
        seedDb.SaveChanges();

        var classA = new SchoolClass { Name = "1.A", SchoolId = schoolA.Id };
        var classB = new SchoolClass { Name = "1.A", SchoolId = schoolB.Id };
        seedDb.SchoolClasses.AddRange(classA, classB);
        seedDb.SaveChanges();

        seedDb.Students.AddRange(
            new StudentProfile { Person = new Person { FirstName = "Žák", LastName = "Školy A", SchoolId = schoolA.Id }, SchoolClassId = classA.Id, SchoolId = schoolA.Id },
            new StudentProfile { Person = new Person { FirstName = "Žák", LastName = "Školy B", SchoolId = schoolB.Id }, SchoolClassId = classB.Id, SchoolId = schoolB.Id });
        seedDb.Users.AddRange(
            new User { Username = "admin.a", PasswordHash = "x", ActiveRole = seedDb.Role(UserRole.Admin, schoolA.Id), SchoolId = schoolA.Id },
            new User { Username = "admin.b", PasswordHash = "x", ActiveRole = seedDb.Role(UserRole.Admin, schoolB.Id), SchoolId = schoolB.Id });
        seedDb.Events.AddRange(
            new Event { Title = "Akce A", SchoolId = schoolA.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1) },
            new Event { Title = "Akce B", SchoolId = schoolB.Id, StartAt = DateTimeOffset.UtcNow, EndAt = DateTimeOffset.UtcNow.AddHours(1) });
        seedDb.SaveChanges();

        return (options, schoolA.Id, schoolB.Id);
    }

    [Fact]
    public void AdminOfSchoolASeesOnlySchoolAsStudents()
    {
        var (options, schoolAId, _) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(schoolAId));

        var students = db.Students.ToList();

        Assert.Single(students);
        Assert.Equal("Školy A", students[0].Person!.LastName);
    }

    [Fact]
    public void AdminOfSchoolBSeesOnlySchoolBsStudents()
    {
        var (options, _, schoolBId) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(schoolBId));

        var students = db.Students.ToList();

        Assert.Single(students);
        Assert.Equal("Školy B", students[0].Person!.LastName);
    }

    [Fact]
    public void AdminOfSchoolACannotSeeSchoolBsUsers()
    {
        var (options, schoolAId, _) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(schoolAId));

        var usernames = db.Users.Select(u => u.Username).ToList();

        Assert.Contains("admin.a", usernames);
        Assert.DoesNotContain("admin.b", usernames);
    }

    [Fact]
    public void SuperAdminSeesStudentsAcrossAllSchools()
    {
        var (options, _, _) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: true));

        var students = db.Students.ToList();

        Assert.Equal(2, students.Count);
    }

    [Fact]
    public void AdminOfSchoolASeesOnlySchoolAsEvents()
    {
        var (options, schoolAId, _) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(schoolAId));

        var events = db.Events.ToList();

        Assert.Single(events);
        Assert.Equal("Akce A", events[0].Title);
    }

    [Fact]
    public void RequestWithNoSchoolIdAndNotSuperAdminSeesNothing()
    {
        // Shape of an unauthenticated/misconfigured request — must fail closed
        // (see no rows), never fail open (see everyone's data).
        var (options, _, _) = SeedTwoSchools();
        using var db = new AppDbContext(options, new FakeCurrentSchoolContext(null, isSuperAdmin: false));

        Assert.Empty(db.Students.ToList());
    }
}
