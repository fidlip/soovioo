using Microsoft.EntityFrameworkCore;
using Sova.Api.Attendance;
using Sova.Api.Auth;
using Sova.Api.Evidence;
using Sova.Api.Grades;
using Sova.Api.Messaging;
using Sova.Api.Reference;
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
    public DbSet<SchoolPeriod> SchoolPeriods => Set<SchoolPeriod>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();

    // Global matrika reference data (RAST/RAOR/RAUJ) — not tenant-scoped,
    // shared by every school. Seeded once from CSV at startup; see
    // Program.cs SeedReferenceData and Data/Seed/*.csv.
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ParentStudent>().HasKey(ps => new { ps.UserId, ps.StudentId });

        modelBuilder.Entity<Country>().HasKey(c => c.Code);
        modelBuilder.Entity<District>().HasKey(d => d.Code);
        modelBuilder.Entity<Municipality>().HasKey(m => m.Code);
        // Restrict, not the default Cascade: deleting a district/country must
        // never silently delete the municipalities/students that reference
        // it — this is read-only reference data, not something a user edits.
        modelBuilder.Entity<Municipality>()
            .HasOne(m => m.District)
            .WithMany()
            .HasForeignKey(m => m.DistrictCode)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Student>()
            .HasOne(s => s.Municipality)
            .WithMany()
            .HasForeignKey(s => s.MunicipalityCode)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Student>()
            .HasOne(s => s.Country)
            .WithMany()
            .HasForeignKey(s => s.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

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
        modelBuilder.Entity<SchoolPeriod>().HasQueryFilter(p =>
            currentSchool.IsSuperAdmin || p.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<SchoolPeriod>().HasIndex(p => new { p.SchoolId, p.PeriodNumber }).IsUnique();
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
                Street = "Školní 8",
                City = "Praha 1",
                PostalCode = "110 00",
                DirectorName = "Mgr. Jana Ředitelová",
                Email = "skola@demo.cz",
                Phone = "+420 123 456 789",
            });

        modelBuilder.Entity<SchoolClass>().HasData(
            new SchoolClass { Id = 1, Name = "5.A", SchoolId = 1, Rocnik = 5, TeachingLanguage = "čeština" });

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
                CitizenshipQualifier = "3", // RAKO: Občan ČR
                // CountryCode/MunicipalityCode NOT set here on purpose: they're
                // FKs into Country/Municipality, which are seeded from CSV at
                // runtime (Program.cs), not via this migration-time HasData —
                // 6400+ rows of C# object initializers in a migration isn't
                // reasonable. Setting the FK here would make `dotnet ef
                // database update` fail (the referenced row doesn't exist yet
                // at migration time). Program.cs backfills both once the
                // reference data is in, after Migrate() has run.
                Street = "Žákovská 12",
                PostalCode = "110 00",
                CatchmentArea = true,
                EnrollmentDate = new DateOnly(2020, 9, 1),
                ContactPhone = "+420 777 123 456",
                ContactEmail = "rodic.novak@example.cz",
            });

        modelBuilder.Entity<Subject>().HasData(
            new Subject { Id = 1, Name = "Matematika", SchoolId = 1, Abbreviation = "M" },
            new Subject { Id = 2, Name = "Český jazyk", SchoolId = 1, Abbreviation = "Čj" },
            new Subject { Id = 3, Name = "Anglický jazyk", SchoolId = 1, Abbreviation = "Aj" });

        modelBuilder.Entity<TimetableEntry>().HasData(
            new TimetableEntry { Id = 1, SchoolId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, WeekParity = WeekParity.Every },
            new TimetableEntry { Id = 2, SchoolId = 1, SchoolClassId = 1, SubjectId = 2, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 2, WeekParity = WeekParity.Every },
            new TimetableEntry { Id = 3, SchoolId = 1, SchoolClassId = 1, SubjectId = 3, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 1, WeekParity = WeekParity.Odd },
            new TimetableEntry { Id = 4, SchoolId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Wednesday, PeriodNumber = 3, WeekParity = WeekParity.Every });
        // TeacherId NOT set here on purpose: User rows (incl. "ucitel") don't
        // exist yet at migration time, same reasoning as the Student
        // Country/MunicipalityCode comment above — Program.cs SeedUsers
        // backfills it once the demo teacher account exists.

        // Typical ZŠ bell schedule — same times for every class, so this
        // lives once per school rather than being re-entered per entry.
        modelBuilder.Entity<SchoolPeriod>().HasData(
            new SchoolPeriod { Id = 1, SchoolId = 1, PeriodNumber = 1, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(8, 45) },
            new SchoolPeriod { Id = 2, SchoolId = 1, PeriodNumber = 2, StartTime = new TimeOnly(8, 55), EndTime = new TimeOnly(9, 40) },
            new SchoolPeriod { Id = 3, SchoolId = 1, PeriodNumber = 3, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(10, 45) },
            new SchoolPeriod { Id = 4, SchoolId = 1, PeriodNumber = 4, StartTime = new TimeOnly(10, 55), EndTime = new TimeOnly(11, 40) },
            new SchoolPeriod { Id = 5, SchoolId = 1, PeriodNumber = 5, StartTime = new TimeOnly(11, 50), EndTime = new TimeOnly(12, 35) },
            new SchoolPeriod { Id = 6, SchoolId = 1, PeriodNumber = 6, StartTime = new TimeOnly(12, 45), EndTime = new TimeOnly(13, 30) });
    }
}
