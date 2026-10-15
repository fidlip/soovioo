using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
using Sova.Api.Auth;
using Sova.Api.Evidence;
using Sova.Api.Grades;
using Sova.Api.Messaging;
using Sova.Api.Timetable;

namespace Sova.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentSchoolContext currentSchool)
    : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParentStudent>().HasKey(ps => new { ps.UserId, ps.StudentId });

        modelBuilder.Entity<School>().HasIndex(s => s.Slug).IsUnique();

        // Username unique per school; SuperAdmin usernames (SchoolId null) unique globally.
        modelBuilder.Entity<User>().HasIndex(u => new { u.SchoolId, u.Username }).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique()
            .HasFilter("\"SchoolId\" IS NULL");

        // Every tenant-scoped entity is filtered to the caller's school by
        // default — a missing `Where` in a controller can't leak another
        // school's data. SuperAdmin requests bypass this for cross-school
        // management. Login (unauthenticated) uses IgnoreQueryFilters()
        // explicitly to look a user up by (school, username).
        modelBuilder.Entity<User>().HasQueryFilter(u =>
            currentSchool.IsSuperAdmin || u.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<SchoolClass>().HasQueryFilter(c =>
            currentSchool.IsSuperAdmin || c.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Subject>().HasQueryFilter(s =>
            currentSchool.IsSuperAdmin || s.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Student>().HasQueryFilter(s =>
            currentSchool.IsSuperAdmin || s.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Grade>().HasQueryFilter(g =>
            currentSchool.IsSuperAdmin || g.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Absence>().HasQueryFilter(a =>
            currentSchool.IsSuperAdmin || a.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<TimetableEntry>().HasQueryFilter(t =>
            currentSchool.IsSuperAdmin || t.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Message>().HasQueryFilter(m =>
            currentSchool.IsSuperAdmin || m.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<ParentStudent>().HasQueryFilter(ps =>
            currentSchool.IsSuperAdmin || ps.SchoolId == currentSchool.SchoolId);

        modelBuilder.Entity<School>().HasData(
            new School
            {
                Id = 1,
                Name = "Demo škola",
                Slug = "demo",
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                // RED_IZO/IZO straight from MŠMT's own matrika test-portal
                // screenshots (the "Osmá testová škola" walkthrough examples),
                // so this is recognizably a documented test fixture rather
                // than a made-up, real-looking identifier.
                RedIzo = "700000089",
                Izo = "308000048",
            });

        modelBuilder.Entity<SchoolClass>().HasData(
            new SchoolClass { Id = 1, Name = "5.A", SchoolId = 1, Rocnik = 5 });

        modelBuilder.Entity<Student>().HasData(
            new Student
            {
                Id = 1,
                FirstName = "Jan",
                LastName = "Novák",
                SchoolClassId = 1,
                SchoolId = 1,
                DateOfBirth = new DateOnly(2015, 3, 10),
                Gender = Gender.Male,
                // RodneCislo intentionally left unset, to exercise the
                // pseudo-RODC construction path in MatrikaExportService.
            });

        modelBuilder.Entity<Subject>().HasData(
            new Subject { Id = 1, Name = "Matematika", SchoolId = 1 },
            new Subject { Id = 2, Name = "Český jazyk", SchoolId = 1 },
            new Subject { Id = 3, Name = "Anglický jazyk", SchoolId = 1 });

        modelBuilder.Entity<TimetableEntry>().HasData(
            new TimetableEntry { Id = 1, SchoolId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, WeekParity = WeekParity.Every },
            new TimetableEntry { Id = 2, SchoolId = 1, SchoolClassId = 1, SubjectId = 2, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 2, WeekParity = WeekParity.Every },
            new TimetableEntry { Id = 3, SchoolId = 1, SchoolClassId = 1, SubjectId = 3, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 1, WeekParity = WeekParity.Odd },
            new TimetableEntry { Id = 4, SchoolId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Wednesday, PeriodNumber = 3, WeekParity = WeekParity.Every });
    }
}
