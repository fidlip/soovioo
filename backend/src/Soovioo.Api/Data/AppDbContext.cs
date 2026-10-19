using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Attendance;
using Soovioo.Api.Auth;
using Soovioo.Api.Behavior;
using Soovioo.Api.Events;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Grades;
using Soovioo.Api.Homework;
using Soovioo.Api.Messaging;
using Soovioo.Api.Payments;
using Soovioo.Api.Reference;
using Soovioo.Api.Timetable;

namespace Soovioo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentSchoolContext currentSchool)
    : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<SchoolPeriod> SchoolPeriods => Set<SchoolPeriod>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MessageBroadcast> MessageBroadcasts => Set<MessageBroadcast>();
    public DbSet<Support.SupportSession> SupportSessions => Set<Support.SupportSession>();
    public DbSet<Audit.AuditLogEntry> AuditLogEntries => Set<Audit.AuditLogEntry>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSchoolClass> EventSchoolClasses => Set<EventSchoolClass>();
    public DbSet<EventTeacher> EventTeachers => Set<EventTeacher>();
    public DbSet<EventParent> EventParents => Set<EventParent>();
    public DbSet<EventStudent> EventStudents => Set<EventStudent>();
    public DbSet<User> Users => Set<User>();
    public DbSet<ParentStudent> ParentStudents => Set<ParentStudent>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
    public DbSet<ParentProfile> ParentProfiles => Set<ParentProfile>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Homework.Homework> Homework => Set<Homework.Homework>();
    public DbSet<HomeworkStudent> HomeworkStudents => Set<HomeworkStudent>();
    public DbSet<HomeworkCompletion> HomeworkCompletions => Set<HomeworkCompletion>();
    public DbSet<HomeworkAttachment> HomeworkAttachments => Set<HomeworkAttachment>();
    public DbSet<BehaviorRecord> BehaviorRecords => Set<BehaviorRecord>();
    public DbSet<BehaviorClassification> BehaviorClassifications => Set<BehaviorClassification>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentRecipient> PaymentRecipients => Set<PaymentRecipient>();
    public DbSet<PaymentSchoolClass> PaymentSchoolClasses => Set<PaymentSchoolClass>();
    public DbSet<PaymentParent> PaymentParents => Set<PaymentParent>();
    public DbSet<PaymentStudent> PaymentStudents => Set<PaymentStudent>();

    // Global matrika reference data (RAST/RAOR/RAUJ) — not tenant-scoped,
    // shared by every school. Seeded once from CSV at startup; see
    // Program.cs SeedReferenceData and Data/Seed/*.csv.
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .HasOne(p => p.User).WithOne(u => u.Person)
            .HasForeignKey<Person>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TeacherProfile>().HasKey(t => t.PersonId);
        modelBuilder.Entity<TeacherProfile>()
            .HasOne(t => t.Person).WithOne(p => p.TeacherProfile)
            .HasForeignKey<TeacherProfile>(t => t.PersonId);
        modelBuilder.Entity<ParentProfile>().HasKey(pp => pp.PersonId);
        modelBuilder.Entity<ParentProfile>()
            .HasOne(pp => pp.Person).WithOne(p => p.ParentProfile)
            .HasForeignKey<ParentProfile>(pp => pp.PersonId);
        modelBuilder.Entity<ParentStudent>().HasKey(ps => new { ps.ParentProfileId, ps.StudentId });
        modelBuilder.Entity<ParentStudent>()
            .HasOne(ps => ps.ParentProfile).WithMany(pp => pp.Children)
            .HasForeignKey(ps => ps.ParentProfileId);
        modelBuilder.Entity<ConversationParticipant>().HasKey(p => new { p.ConversationId, p.UserId });
        // A deleted account leaves the conversations it wrote in readable for
        // the others — just without a link to the sender.
        modelBuilder.Entity<Message>()
            .HasOne(m => m.SenderUser).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.Student).WithMany().OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.Broadcast).WithMany(b => b.Conversations).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<MessageBroadcast>()
            .HasOne(b => b.SenderUser).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Support.SupportSession>()
            .HasOne(s => s.SuperAdminUser).WithMany().OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Audit.AuditLogEntry>().HasIndex(e => new { e.SchoolId, e.At });
        modelBuilder.Entity<Audit.AuditLogEntry>().HasIndex(e => new { e.UserId, e.At });
        modelBuilder.Entity<Audit.AuditLogEntry>().HasIndex(e => e.At);
        modelBuilder.Entity<Audit.AuditLogEntry>().HasIndex(e => e.SupportSessionId);
        modelBuilder.Entity<HomeworkStudent>().HasKey(hs => new { hs.HomeworkId, hs.StudentId });
        modelBuilder.Entity<HomeworkCompletion>().HasKey(hc => new { hc.HomeworkId, hc.StudentId });
        modelBuilder.Entity<BehaviorClassification>()
            .HasKey(c => new { c.StudentId, c.SchoolYearStartYear, c.HalfYear });

        // Audience recipient join tables — composite keys, same shape as
        // ParentStudent above. Everyone/OnlyMe events keep these empty;
        // only Selected ever populates them (see EventsController.SetRecipients).
        modelBuilder.Entity<EventSchoolClass>().HasKey(x => new { x.EventId, x.SchoolClassId });
        modelBuilder.Entity<EventTeacher>().HasKey(x => new { x.EventId, x.UserId });
        modelBuilder.Entity<EventParent>().HasKey(x => new { x.EventId, x.UserId });
        modelBuilder.Entity<EventStudent>().HasKey(x => new { x.EventId, x.StudentId });
        // Default (0) is Everyone — see EventAudience's own doc comment for why.
        modelBuilder.Entity<Event>().Property(e => e.Audience).HasDefaultValue(EventAudience.Everyone);

        // Same shape, for Payment's smaller audience/recipient join tables.
        modelBuilder.Entity<PaymentSchoolClass>().HasKey(x => new { x.PaymentId, x.SchoolClassId });
        modelBuilder.Entity<PaymentParent>().HasKey(x => new { x.PaymentId, x.UserId });
        modelBuilder.Entity<PaymentStudent>().HasKey(x => new { x.PaymentId, x.StudentId });
        modelBuilder.Entity<Payment>().Property(p => p.Audience).HasDefaultValue(PaymentAudience.Everyone);

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
        // One-to-one, FK on the Student side (EF Core enforces uniqueness of
        // Student.UserId automatically for this shape). SetNull: deleting the
        // login account (e.g. an Admin resetting/removing it directly via
        // UsersController) must not fail or cascade into deleting the
        // Student record itself — it just un-links.
        modelBuilder.Entity<Student>()
            .HasOne(s => s.User)
            .WithOne()
            .HasForeignKey<Student>(s => s.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<School>().HasIndex(s => s.Slug).IsUnique();

        // Username unique per school; SuperAdmin usernames (SchoolId null) unique globally.
        modelBuilder.Entity<User>().HasIndex(u => new { u.SchoolId, u.Username }).IsUnique();
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique()
            .HasFilter("\"SchoolId\" IS NULL");
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.FamilyId);
        modelBuilder.Entity<RefreshToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.CalendarToken)
            .IsUnique()
            .HasFilter("\"CalendarToken\" IS NOT NULL");

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
        modelBuilder.Entity<Classroom>().Property(c => c.Type).HasConversion<string>();
        modelBuilder.Entity<Classroom>().HasQueryFilter(c =>
            currentSchool.IsSuperAdmin || c.SchoolId == currentSchool.SchoolId);
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
        modelBuilder.Entity<Conversation>().HasQueryFilter(c =>
            currentSchool.IsSuperAdmin || c.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<MessageBroadcast>().HasQueryFilter(b =>
            currentSchool.IsSuperAdmin || b.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<ConversationParticipant>().HasQueryFilter(p =>
            currentSchool.IsSuperAdmin || p.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Message>().HasQueryFilter(m =>
            currentSchool.IsSuperAdmin || m.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<MessageAttachment>().HasQueryFilter(a =>
            currentSchool.IsSuperAdmin || a.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Homework.Homework>().HasQueryFilter(h =>
            currentSchool.IsSuperAdmin || h.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<HomeworkStudent>().HasQueryFilter(hs =>
            currentSchool.IsSuperAdmin || hs.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<HomeworkCompletion>().HasQueryFilter(hc =>
            currentSchool.IsSuperAdmin || hc.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<HomeworkAttachment>().HasQueryFilter(ha =>
            currentSchool.IsSuperAdmin || ha.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<BehaviorRecord>().HasQueryFilter(r =>
            currentSchool.IsSuperAdmin || r.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<BehaviorClassification>().HasQueryFilter(c =>
            currentSchool.IsSuperAdmin || c.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Event>().HasQueryFilter(e =>
            currentSchool.IsSuperAdmin || e.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<EventSchoolClass>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<EventTeacher>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<EventParent>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<EventStudent>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Person>().HasQueryFilter(p =>
            currentSchool.IsSuperAdmin || p.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<TeacherProfile>().HasQueryFilter(t =>
            currentSchool.IsSuperAdmin || t.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<ParentProfile>().HasQueryFilter(pp =>
            currentSchool.IsSuperAdmin || pp.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<ParentStudent>().HasQueryFilter(ps =>
            currentSchool.IsSuperAdmin || ps.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Payment>().HasQueryFilter(p =>
            currentSchool.IsSuperAdmin || p.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<PaymentRecipient>().HasQueryFilter(r =>
            currentSchool.IsSuperAdmin || r.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<PaymentSchoolClass>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<PaymentParent>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<PaymentStudent>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);

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

        modelBuilder.Entity<Classroom>().HasData(
            new Classroom { Id = 1, Name = "12", SchoolId = 1, Type = ClassroomType.Homeroom },
            new Classroom { Id = 2, Name = "Tělocvična", ShortName = "Tv", SchoolId = 1, Type = ClassroomType.Gym, Capacity = 30 });

        modelBuilder.Entity<TimetableEntry>().HasData(
            new TimetableEntry { Id = 1, SchoolId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, WeekParity = WeekParity.Every, ClassroomId = 1 },
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
