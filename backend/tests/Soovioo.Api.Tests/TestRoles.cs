using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;

namespace Soovioo.Api.Tests;

/// <summary>The built-in roles of a test school (created with their default
/// permissions on first use) and principals acting in them.</summary>
internal static class TestRoles
{
    public static Role Role(this AppDbContext db, UserRole builtIn, int schoolId = 1)
    {
        var existing = db.Roles.Local.FirstOrDefault(r => r.SchoolId == schoolId && r.BuiltIn == builtIn)
            ?? db.Roles.IgnoreQueryFilters().Include(r => r.Permissions)
                .FirstOrDefault(r => r.SchoolId == schoolId && r.BuiltIn == builtIn);
        if (existing is not null)
        {
            return existing;
        }
        var school = new global::Soovioo.Api.Evidence.School { Name = "", Slug = "" };
        RoleSeeder.AddMissing(school);
        foreach (var role in school.Roles)
        {
            role.SchoolId = schoolId;
            db.Roles.Add(role);
        }
        db.SaveChanges();
        return school.Roles.Single(r => r.BuiltIn == builtIn);
    }

    /// <summary>A user acting in their active role, as the token would say.</summary>
    public static ClaimsPrincipal PrincipalOf(User user, string? name = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Name, name ?? user.Username) };
        if (user.ActiveRole is { } role)
        {
            claims.Add(new Claim(ClaimTypes.Role, role.Kind.ToString()));
            claims.Add(new Claim(PermissionAccess.RoleIdClaim, role.Id.ToString()));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, nameof(UserRole.SuperAdmin)));
        }
        if (user.SchoolId is { } schoolId)
        {
            claims.Add(new Claim("schoolId", schoolId.ToString()));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Account <paramref name="userId"/> in <paramref name="builtIn"/> —
    /// created (or given the role) when the test hasn't.</summary>
    public static ClaimsPrincipal Principal(
        AppDbContext db, int userId, UserRole builtIn, int schoolId = 1, string? name = null)
    {
        var role = db.Role(builtIn, schoolId);
        var user = db.Users.Local.FirstOrDefault(u => u.Id == userId)
            ?? db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            user = new User { Id = userId, Username = $"test{userId}", PasswordHash = "x", SchoolId = schoolId };
            db.Users.Add(user);
        }
        user.ActiveRole = role;
        db.SaveChanges();
        return PrincipalOf(user, name);
    }

    /// <summary>A lesson of <paramref name="teacherId"/> in the class — a
    /// teacher's "own classes" (and subjects) come from the timetable.</summary>
    public static void Teaches(this AppDbContext db, int teacherId, int schoolClassId, int subjectId = 0, int schoolId = 1)
    {
        db.TimetableEntries.Add(new global::Soovioo.Api.Timetable.TimetableEntry
        {
            SchoolId = schoolId, SchoolClassId = schoolClassId, SubjectId = subjectId, TeacherId = teacherId,
            DayOfWeek = DayOfWeek.Monday, PeriodNumber = db.TimetableEntries.Local.Count + 1,
        });
        db.SaveChanges();
    }

    public static ControllerContext Context(ClaimsPrincipal principal) =>
        new() { HttpContext = new DefaultHttpContext { User = principal } };

    public static ControllerContext Context(AppDbContext db, int userId, UserRole builtIn, int schoolId = 1) =>
        Context(Principal(db, userId, builtIn, schoolId));
}
