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
using Soovioo.Api.Timetable.Rvp;

namespace Soovioo.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentSchoolContext currentSchool)
    : DbContext(options)
{
    public DbSet<School> Schools => Set<School>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<StudentProfile> Students => Set<StudentProfile>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Classroom> Classrooms => Set<Classroom>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<TimetableEntry> TimetableEntries => Set<TimetableEntry>();
    public DbSet<TimetableVersion> TimetableVersions => Set<TimetableVersion>();
    public DbSet<CurriculumItem> CurriculumItems => Set<CurriculumItem>();
    public DbSet<ClassCurriculumItem> ClassCurriculumItems => Set<ClassCurriculumItem>();
    public DbSet<SubjectRvpArea> SubjectRvpAreas => Set<SubjectRvpArea>();
    public DbSet<TeachingAssignment> TeachingAssignments => Set<TeachingAssignment>();
    public DbSet<StudentGroup> StudentGroups => Set<StudentGroup>();
    public DbSet<GroupDivision> GroupDivisions => Set<GroupDivision>();
    public DbSet<StudentGroupClass> StudentGroupClasses => Set<StudentGroupClass>();
    public DbSet<StudentGroupMember> StudentGroupMembers => Set<StudentGroupMember>();
    public DbSet<TimetableGroupChange> TimetableGroupChanges => Set<TimetableGroupChange>();
    public DbSet<TimetableGeneration> TimetableGenerations => Set<TimetableGeneration>();
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
    public DbSet<TeacherQualification> TeacherQualifications => Set<TeacherQualification>();
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
    public DbSet<Demo.DemoEmail> DemoEmails => Set<Demo.DemoEmail>();
    public DbSet<VisibilityRule> VisibilityRules => Set<VisibilityRule>();
    public DbSet<VisibilityChoice> VisibilityChoices => Set<VisibilityChoice>();
    public DbSet<Retention.RetentionPolicy> RetentionPolicies => Set<Retention.RetentionPolicy>();
    public DbSet<Retention.StaffHandover> StaffHandovers => Set<Retention.StaffHandover>();
    public DbSet<Notifications.Notification> Notifications => Set<Notifications.Notification>();
    public DbSet<Notifications.PushDevice> PushDevices => Set<Notifications.PushDevice>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    /// <summary>The day the former-pupil filter compares
    /// <see cref="StudentProfile.LeftAt"/> with (read per query).</summary>
    public DateOnly Today { get; set; } = Common.SchoolClock.Today(TimeProvider.System);

    /// <summary>Former pupils (<see cref="StudentProfile.LeftAt"/> before
    /// <see cref="Today"/>) are left out of every query — the app shows
    /// pupils of the school. Administrace → Archiv a výmaz údajů and the retention job
    /// switch this on to reach them; the tenant filter still applies.</summary>
    public bool IncludeFormerStudents { get; set; }

    // Not tenant-scoped: SuperAdmin-only lists, and the rows may outlive
    // their demo school (see the classes).
    public DbSet<Demo.DemoContact> DemoContacts => Set<Demo.DemoContact>();
    public DbSet<Demo.DemoFeedback> DemoFeedback => Set<Demo.DemoFeedback>();

    // Global matrika reference data (RAST/RAOR/RAUJ) — not tenant-scoped,
    // shared by every school. Seeded once from CSV at startup; see
    // Program.cs SeedReferenceData and Data/Seed/*.csv.
    public DbSet<Country> Countries => Set<Country>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Municipality> Municipalities => Set<Municipality>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var rolesChanged = RolesChanged();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (rolesChanged)
        {
            RolePermissionCache.Of(this)?.Clear();
        }
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var rolesChanged = RolesChanged();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (rolesChanged)
        {
            RolePermissionCache.Of(this)?.Clear();
        }
        return result;
    }

    /// <summary>A role, its permissions or who has it — what
    /// <see cref="RolePermissionCache"/> holds.</summary>
    private bool RolesChanged() =>
        ChangeTracker.Entries().Any(e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && (e.Entity is Role or RolePermission || e.Metadata.Name == "UserRoles"
                || (e.Entity is User && e.State == EntityState.Deleted)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SetNull: a Person is a record of its own (Administrace → Osoby)
        // and outlives its account.
        modelBuilder.Entity<Person>()
            .HasOne(p => p.User).WithOne(u => u.Person)
            .HasForeignKey<Person>(p => p.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<StudentProfile>()
            .HasOne(s => s.Person).WithOne(p => p.Student)
            .HasForeignKey<StudentProfile>(s => s.PersonId)
            .OnDelete(DeleteBehavior.Restrict);
        // A pupil's name lives on their Person — loaded wherever they are.
        modelBuilder.Entity<StudentProfile>().Navigation(s => s.Person).AutoInclude();
        modelBuilder.Entity<Person>().Property(p => p.PhotoStatus).HasConversion<string>();
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
        modelBuilder.Entity<StudentProfile>()
            .HasOne(s => s.Municipality)
            .WithMany()
            .HasForeignKey(s => s.MunicipalityCode)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<StudentProfile>()
            .HasOne(s => s.Country)
            .WithMany()
            .HasForeignKey(s => s.CountryCode)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<School>().HasIndex(s => s.Slug).IsUnique();
        modelBuilder.Entity<School>().HasIndex(s => s.ExpiresAt).HasFilter("\"IsDemo\"");

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
        modelBuilder.Entity<StudentProfile>().HasQueryFilter(s =>
            (currentSchool.IsSuperAdmin || s.SchoolId == currentSchool.SchoolId)
            && (IncludeFormerStudents || s.LeftAt == null || s.LeftAt >= Today));
        modelBuilder.Entity<Grade>().HasQueryFilter(g =>
            currentSchool.IsSuperAdmin || g.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Absence>().HasQueryFilter(a =>
            currentSchool.IsSuperAdmin || a.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<TimetableEntry>().HasQueryFilter(t =>
            currentSchool.IsSuperAdmin || t.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<TimetableEntry>()
            .HasOne(t => t.TimetableVersion).WithMany(v => v.Entries)
            .HasForeignKey(t => t.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TimetableVersion>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.SchoolId, x.Status, x.ValidFrom });
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<CurriculumItem>(e =>
        {
            e.Property(x => x.HoursPerWeek).HasPrecision(4, 1);
            e.HasIndex(x => new { x.TimetableVersionId, x.Grade, x.SubjectId }).IsUnique();
            e.HasOne(x => x.TimetableVersion).WithMany(v => v.Curriculum)
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<ClassCurriculumItem>(e =>
        {
            e.Property(x => x.HoursPerWeek).HasPrecision(4, 1);
            e.HasIndex(x => new { x.TimetableVersionId, x.SchoolClassId, x.SubjectId }).IsUnique();
            e.HasOne(x => x.TimetableVersion).WithMany(v => v.ClassCurriculum)
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<SubjectRvpArea>(e =>
        {
            e.HasIndex(x => new { x.SubjectId, x.ProgramCode }).IsUnique();
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<TeachingAssignment>(e =>
        {
            e.HasIndex(x => new { x.TimetableVersionId, x.SchoolClassId, x.SubjectId }).IsUnique();
            e.HasOne(x => x.TimetableVersion).WithMany(v => v.Assignments)
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<StudentGroup>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.HoursPerWeek).HasPrecision(4, 1);
            e.HasOne(x => x.TimetableVersion).WithMany(v => v.Groups)
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.GroupDivision).WithMany(d => d.Groups)
                .HasForeignKey(x => x.GroupDivisionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Classroom).WithMany().HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<GroupDivision>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.HoursPerWeek).HasPrecision(4, 1);
            e.HasOne(x => x.TimetableVersion).WithMany(v => v.Divisions)
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CoversSubject).WithMany().HasForeignKey(x => x.CoversSubjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<StudentGroupClass>(e =>
        {
            e.HasKey(x => new { x.StudentGroupId, x.SchoolClassId });
            e.HasOne(x => x.StudentGroup).WithMany(g => g.Classes)
                .HasForeignKey(x => x.StudentGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SchoolClass).WithMany()
                .HasForeignKey(x => x.SchoolClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<TimetableGroupChange>(e =>
        {
            e.HasKey(x => new { x.StudentGroupId, x.StudentId });
            e.HasOne(x => x.StudentGroup).WithMany()
                .HasForeignKey(x => x.StudentGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany()
                .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<StudentGroupMember>(e =>
        {
            e.HasKey(x => new { x.StudentGroupId, x.StudentId });
            e.HasOne(x => x.StudentGroup).WithMany(g => g.Members)
                .HasForeignKey(x => x.StudentGroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany()
                .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<TimetableEntry>()
            .HasOne(t => t.StudentGroup).WithMany()
            .HasForeignKey(t => t.StudentGroupId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<TimetableGeneration>(e =>
        {
            e.HasOne(x => x.TimetableVersion).WithMany()
                .HasForeignKey(x => x.TimetableVersionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Progress).HasColumnType("jsonb").HasConversion(
                p => System.Text.Json.JsonSerializer.Serialize(p, (System.Text.Json.JsonSerializerOptions?)null),
                s => System.Text.Json.JsonSerializer.Deserialize<TimetableGenerator.Progress>(s, (System.Text.Json.JsonSerializerOptions?)null));
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<Subject>().OwnsMany(s => s.Classrooms, c =>
        {
            c.ToTable("SubjectClassrooms");
            c.Property(x => x.Type).HasConversion<string>();
            c.HasOne(x => x.Classroom).WithMany().HasForeignKey(x => x.ClassroomId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<SchoolClass>().HasOne(c => c.Homeroom).WithMany()
            .HasForeignKey(c => c.HomeroomId).OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity<Homework.Homework>(e =>
        {
            e.HasQueryFilter(h => currentSchool.IsSuperAdmin || h.SchoolId == currentSchool.SchoolId);
            e.HasOne(h => h.SchoolClass).WithMany().HasForeignKey(h => h.SchoolClassId).OnDelete(DeleteBehavior.Cascade);
            // A group with homework isn't deleted, see StudentGroupsController.
            e.HasOne(h => h.StudentGroup).WithMany().HasForeignKey(h => h.StudentGroupId).OnDelete(DeleteBehavior.Restrict);
        });
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
        modelBuilder.Entity<TeacherProfile>().Property(t => t.ContractedHours).HasPrecision(4, 1);
        modelBuilder.Entity<TeacherProfile>(e =>
        {
            e.Ignore(t => t.TeachingDays);
            e.Property(t => t.Availability).HasColumnType("jsonb").HasConversion(
                a => System.Text.Json.JsonSerializer.Serialize(a, (System.Text.Json.JsonSerializerOptions?)null),
                s => System.Text.Json.JsonSerializer.Deserialize<List<AvailabilitySlot>>(s, (System.Text.Json.JsonSerializerOptions?)null) ?? new List<AvailabilitySlot>(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<AvailabilitySlot>>(
                    (a, b) => a!.SequenceEqual(b!),
                    a => a.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
                    a => a.ToList()));
        });
        modelBuilder.Entity<TeacherQualification>(e =>
        {
            e.HasIndex(x => new { x.TeacherProfileId, x.SubjectId });
            e.HasOne(x => x.TeacherProfile).WithMany(t => t.Qualifications)
                .HasForeignKey(x => x.TeacherProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<ParentProfile>().HasQueryFilter(pp =>
            currentSchool.IsSuperAdmin || pp.SchoolId == currentSchool.SchoolId);
        // A former pupil's parents lose access to them with the rest of the
        // app: every parent check goes through these links.
        modelBuilder.Entity<ParentStudent>().HasQueryFilter(ps =>
            (currentSchool.IsSuperAdmin || ps.SchoolId == currentSchool.SchoolId)
            && Set<StudentProfile>().Any(s => s.Id == ps.StudentId));
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
        modelBuilder.Entity<Demo.DemoEmail>().HasQueryFilter(x =>
            currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        modelBuilder.Entity<Demo.DemoEmail>().HasIndex(x => x.SchoolId);
        modelBuilder.Entity<Demo.DemoContact>().HasIndex(x => x.SchoolId);
        modelBuilder.Entity<Demo.DemoFeedback>().HasIndex(x => x.CreatedAt);

        modelBuilder.Entity<StudentProfile>().Property(s => s.LeaveReason).HasConversion<string>();
        modelBuilder.Entity<StudentProfile>().PrimitiveCollection(s => s.ErasedCategories).ElementType().HasConversion<string>();
        modelBuilder.Entity<User>().HasIndex(u => u.DeactivatedAt).HasFilter("\"DeactivatedAt\" IS NOT NULL");
        modelBuilder.Entity<User>().Property(u => u.LeaveReason).HasConversion<string>();
        modelBuilder.Entity<Retention.StaffHandover>(e =>
        {
            e.Property(x => x.Kind).HasConversion<string>();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<Retention.RetentionPolicy>(e =>
        {
            e.PrimitiveCollection(x => x.StaffLeaveNotices).ElementType().HasConversion<string>();
            e.HasKey(x => x.SchoolId);
            e.Property(x => x.Mode).HasConversion<string>();
            e.HasOne(x => x.School).WithOne().HasForeignKey<Retention.RetentionPolicy>(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.Property(r => r.Kind).HasConversion<string>();
            e.Property(r => r.BuiltIn).HasConversion<string>();
            e.HasIndex(r => new { r.SchoolId, r.Name }).IsUnique();
            e.HasIndex(r => new { r.SchoolId, r.BuiltIn }).IsUnique().HasFilter("\"BuiltIn\" IS NOT NULL");
            e.HasOne(r => r.School).WithMany(s => s.Roles).HasForeignKey(r => r.SchoolId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(r => currentSchool.IsSuperAdmin || r.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<RolePermission>(e =>
        {
            e.HasKey(p => new { p.RoleId, p.Permission });
            e.Property(p => p.Permission).HasConversion<string>();
            e.Property(p => p.Scope).HasConversion<string>();
            e.HasOne<Role>().WithMany(r => r.Permissions).HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<User>().HasMany(u => u.Roles).WithMany(r => r.Users).UsingEntity("UserRoles");
        // NoAction, not Restrict: deleting a school takes its roles and
        // accounts in the same statement. A single role is only deleted once
        // nobody has it.
        modelBuilder.Entity<User>()
            .HasOne(u => u.ActiveRole).WithMany().HasForeignKey(u => u.ActiveRoleId).OnDelete(DeleteBehavior.NoAction);
        modelBuilder.Entity<User>().Navigation(u => u.Roles).AutoInclude();
        modelBuilder.Entity<User>().Navigation(u => u.ActiveRole).AutoInclude();

        modelBuilder.Entity<User>().PrimitiveCollection(u => u.MutedNotifications).ElementType().HasConversion<string>();
        modelBuilder.Entity<User>().PrimitiveCollection(u => u.MutedEmailNotifications).ElementType().HasConversion<string>();
        modelBuilder.Entity<User>().PrimitiveCollection(u => u.MutedPushNotifications).ElementType().HasConversion<string>();
        modelBuilder.Entity<User>().HasIndex(u => u.EmailUnsubscribeToken).IsUnique();
        modelBuilder.Entity<School>().PrimitiveCollection(s => s.ForcedEmailNotifications).ElementType().HasConversion<string>();
        modelBuilder.Entity<Notifications.Notification>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>();
            e.HasIndex(x => new { x.UserId, x.Id });
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.UserId).HasDatabaseName("IX_Notifications_PushPending")
                .HasFilter("\"Push\" AND \"PushedAt\" IS NULL");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<Notifications.PushDevice>(e =>
        {
            e.Property(x => x.Platform).HasConversion<string>();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });

        modelBuilder.Entity<VisibilityRule>(e =>
        {
            e.Property(x => x.Owner).HasConversion<string>();
            e.Property(x => x.Field).HasConversion<string>();
            e.Property(x => x.Audience).HasConversion<string>();
            e.Property(x => x.Min).HasConversion<string>();
            e.Property(x => x.Default).HasConversion<string>();
            e.Property(x => x.Max).HasConversion<string>();
            e.HasIndex(x => new { x.SchoolId, x.Owner, x.Field, x.Audience }).IsUnique();
            e.HasOne(x => x.School).WithMany().HasForeignKey(x => x.SchoolId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });
        modelBuilder.Entity<VisibilityChoice>(e =>
        {
            e.Property(x => x.Owner).HasConversion<string>();
            e.Property(x => x.Field).HasConversion<string>();
            e.Property(x => x.Audience).HasConversion<string>();
            e.Property(x => x.Scope).HasConversion<string>();
            e.HasIndex(x => new { x.PersonId, x.Owner, x.Field, x.Audience }).IsUnique();
            e.HasOne(x => x.Person).WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Cascade);
            e.HasQueryFilter(x => currentSchool.IsSuperAdmin || x.SchoolId == currentSchool.SchoolId);
        });

        modelBuilder.Entity<School>().HasData(
            new School
            {
                Id = 1,
                Name = "Testovací škola",
                // Not "demo" — that slug (and "demo-*") belongs to the
                // "Vyzkoušet" demo schools, see Demo/DemoSlugs.
                Slug = "skola",
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

        modelBuilder.Entity<Subject>().HasData(
            new Subject { Id = 1, Name = "Matematika", SchoolId = 1, Abbreviation = "M" },
            new Subject { Id = 2, Name = "Český jazyk", SchoolId = 1, Abbreviation = "Čj" },
            new Subject { Id = 3, Name = "Anglický jazyk", SchoolId = 1, Abbreviation = "Aj" });

        modelBuilder.Entity<Classroom>().HasData(
            new Classroom { Id = 1, Name = "12", SchoolId = 1, Type = ClassroomType.Homeroom },
            new Classroom { Id = 2, Name = "Tělocvična", ShortName = "Tv", SchoolId = 1, Type = ClassroomType.Gym, Capacity = 30 });

        // The TimetableVersions migration moves ValidFrom to the start of the
        // school year it runs in.
        modelBuilder.Entity<TimetableVersion>().HasData(
            new TimetableVersion
            {
                Id = 1, SchoolId = 1, Name = "Rozvrh 2025/26", ValidFrom = new DateOnly(2025, 9, 1),
                Status = TimetableVersionStatus.Published,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                PublishedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            });

        modelBuilder.Entity<TimetableEntry>().HasData(
            new TimetableEntry { Id = 1, SchoolId = 1, TimetableVersionId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 1, WeekParity = WeekParity.Every, ClassroomId = 1 },
            new TimetableEntry { Id = 2, SchoolId = 1, TimetableVersionId = 1, SchoolClassId = 1, SubjectId = 2, DayOfWeek = DayOfWeek.Monday, PeriodNumber = 2, WeekParity = WeekParity.Every },
            new TimetableEntry { Id = 3, SchoolId = 1, TimetableVersionId = 1, SchoolClassId = 1, SubjectId = 3, DayOfWeek = DayOfWeek.Tuesday, PeriodNumber = 1, WeekParity = WeekParity.Odd },
            new TimetableEntry { Id = 4, SchoolId = 1, TimetableVersionId = 1, SchoolClassId = 1, SubjectId = 1, DayOfWeek = DayOfWeek.Wednesday, PeriodNumber = 3, WeekParity = WeekParity.Every });
        // TeacherId NOT set here on purpose: User rows (incl. "ucitel") don't
        // exist yet at migration time — Program.cs SeedUsers backfills it
        // once the demo teacher account exists.

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
