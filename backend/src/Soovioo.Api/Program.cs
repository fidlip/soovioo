using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Email;
using Soovioo.Api.Evidence;
using Soovioo.Api.Matrika;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Payments;
using Soovioo.Api.People;
using Soovioo.Api.Reference;

QuestPDF.Settings.License = LicenseType.Community;

// Windows-1250 is required (not .NET's default) for the matrika XML export —
// it's not included by default outside .NET Framework.
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers(options =>
    {
        // Validation error keys follow the JSON names (camelCase) the client
        // sends, so the web app can map them back onto its form fields.
        options.ModelMetadataDetailsProviders.Add(new Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.SystemTextJsonValidationMetadataProvider());
        options.Filters.Add<SuperAdminScopeFilter>();
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSchoolContext, HttpCurrentSchoolContext>();
builder.Services.AddScoped<MatrikaExportService>();
builder.Services.AddScoped<JwtTokenFactory>();
builder.Services.AddScoped<RefreshTokenService>();
builder.Services.AddScoped<AuthSessionService>();
builder.Services.AddScoped<Soovioo.Api.Support.SupportSessionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<AuthCookieOptions>(builder.Configuration.GetSection("AuthCookie"));
builder.Services.AddScoped<MessageAttachmentStorageService>();
builder.Services.AddScoped<Soovioo.Api.People.PhotoStorage>();
builder.Services.AddScoped<Soovioo.Api.People.PhotoAccess>();
builder.Services.AddKeyedScoped<IEmailSender, SmtpEmailSender>(SmtpEmailSender.Key);
builder.Services.AddScoped<IEmailSender, Soovioo.Api.Demo.DemoAwareEmailSender>();
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
builder.Services.Configure<AppLinkOptions>(builder.Configuration.GetSection("App"));
builder.Services.Configure<Soovioo.Api.Demo.DemoOptions>(builder.Configuration.GetSection("Demo"));
builder.Services.AddScoped<Soovioo.Api.Demo.DemoDataGenerator>();
builder.Services.AddScoped<Soovioo.Api.Demo.DemoSchoolPurger>();
builder.Services.AddScoped<Soovioo.Api.Retention.AccountEraser>();
builder.Services.AddScoped<Soovioo.Api.Retention.StudentEraser>();
builder.Services.AddScoped<Soovioo.Api.Retention.RetentionJob>();
builder.Services.AddScoped<Soovioo.Api.Retention.LeaveNotifier>();
builder.Services.AddScoped<Soovioo.Api.Notifications.Notifier>();
builder.Services.AddScoped<Soovioo.Api.Notifications.NotificationDigestJob>();
builder.Services.AddScoped<Soovioo.Api.Notifications.PushJob>();
builder.Services.AddPush(builder.Configuration);
builder.Services.AddScoped<Soovioo.Api.Demo.DemoSchoolService>();

// Generating a demo school is the one anonymous request that costs real
// work (seconds of CPU, tens of thousands of rows) — limited per client IP
// (behind Caddy the forwarded one, see ASPNETCORE_FORWARDEDHEADERS_ENABLED).
var demoCreatesPerDay = builder.Configuration.GetValue("Demo:CreatePerIpPerDay", 5);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(Soovioo.Api.Demo.DemoController.CreateRateLimit, http =>
        System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = demoCreatesPerDay,
                Window = TimeSpan.FromDays(1),
            }));
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

// SigningKey is deliberately NOT in appsettings.json — a real secret must
// never ship in source control. For local dev it's set once via
// `dotnet user-secrets set "Jwt:SigningKey" "..."` (see Poznámky k
// prostředí in ROADMAP.md); for any real deployment it must come from the
// environment or a secrets store (e.g. Jwt__SigningKey env var).
var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing Jwt configuration section.");
if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
{
    throw new InvalidOperationException(
        "Jwt:SigningKey is not set. Run `dotnet user-secrets set \"Jwt:SigningKey\" \"<random 32+ char value>\"` " +
        "for local dev, or set the Jwt__SigningKey environment variable for a real deployment.");
}
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            // Access tokens live only 15 minutes (refresh tokens do the
            // long haul) — the default 5-minute skew would stretch that by a
            // third.
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });
builder.Services.AddAuthorization();

// Credentialed CORS: the web app's refresh token is an httpOnly cookie, so
// the auth endpoints are called withCredentials — which forbids a wildcard
// origin. Dev reflects any origin back (the dev server is also reached from
// a phone on the LAN by IP, not just http://localhost:4200), but ONLY while
// actually running in Development (gated below), so a deployment that
// forgets to set ASPNETCORE_ENVIRONMENT=Production doesn't silently inherit
// it. Even then a foreign page can't use the cookie: it's SameSite=Strict
// and refresh/logout also demand the X-Soovioo-Csrf header.
const string devCorsPolicy = "DevClients";
builder.Services.AddCors(options =>
    options.AddPolicy(devCorsPolicy, policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

// Non-dev: explicit allow-list from config (Cors:AllowedOrigins), e.g. the
// school subdomains once a real domain exists. Unset = no CORS policy at
// all, i.e. cross-origin browser requests are rejected by default — fail
// closed rather than fail open while that topology isn't decided yet.
const string prodCorsPolicy = "ProdClients";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
    options.AddPolicy(prodCorsPolicy, policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedUsers(db, app.Configuration.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions());
    BackfillDemoTimetableTeacher(db);
    BackfillDemoClassTeacher(db);
    BackfillDemoPersonNames(db);
    SeedReferenceData(db);
    BackfillDemoStudentReferenceCodes(db);
    VariableSymbols.Backfill(db);
    BackfillSchoolIbans(db);
    BackfillMissingPersons(db);

    // First real Hangfire job in this codebase (Hangfire itself was already
    // wired up via AddHangfire/AddHangfireServer above) — generates each
    // recurring Payment's next installment once its due date arrives. Runs
    // with no HTTP context, hence PaymentRecipientGenerator's explicit
    // IgnoreQueryFilters()+SchoolId filtering instead of relying on the
    // normal per-request query filter. Uses the DI-resolved
    // IRecurringJobManager, not the static RecurringJob facade — the latter
    // reads JobStorage.Current, which isn't set yet this early (it's only
    // initialized once Hangfire's own hosted service starts with app.Run()).
    var recurringJobs = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobs.AddOrUpdate<PaymentRecurringInstallmentsJob>(
        "payment-recurring-installments", job => job.RunAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<RefreshTokenCleanupJob>(
        "refresh-token-cleanup", job => job.RunAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<Soovioo.Api.Audit.AuditLogCleanupJob>(
        "audit-log-cleanup", job => job.RunAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<Soovioo.Api.Demo.DemoSchoolService>(
        "demo-schools-daily", service => service.RunDailyAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<Soovioo.Api.Retention.RetentionJob>(
        "retention-daily", job => job.RunAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<Soovioo.Api.Notifications.NotificationCleanupJob>(
        "notification-cleanup", job => job.RunAsync(), Cron.Daily);
    recurringJobs.AddOrUpdate<Soovioo.Api.Notifications.NotificationDigestJob>(
        "notification-digest", job => job.RunAsync(), "*/15 * * * *");
    recurringJobs.AddOrUpdate<Soovioo.Api.Notifications.PushJob>(
        "notification-push", job => job.RunAsync(), Cron.Minutely);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(app.Environment.IsDevelopment() ? devCorsPolicy : prodCorsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<Soovioo.Api.Audit.AuditMiddleware>();

app.MapControllers();

app.MapHangfireDashboard();

app.Run();

static void SeedUsers(AppDbContext db, SeedOptions seed)
{
    // IgnoreQueryFilters: this runs with no authenticated caller, so the
    // per-school filter would otherwise only ever see SuperAdmin rows and
    // re-seed demo users on every startup.
    if (db.Users.IgnoreQueryFilters().Any())
    {
        return;
    }

    const int demoSchoolId = 1;
    var hasher = new PasswordHasher<User>();

    // A real deployment gets only the built-in SuperAdmin, with a password
    // from the environment (changed on first login) — never the demo
    // accounts with their well-known "<username>123" passwords.
    if (!seed.DemoAccounts)
    {
        if (string.IsNullOrWhiteSpace(seed.SuperAdminPassword) || seed.SuperAdminPassword.Length < 8)
        {
            throw new InvalidOperationException(
                "Empty database and Seed:DemoAccounts is off: set Seed__SuperAdminPassword (8+ chars) " +
                "for the built-in superadmin account's first login.");
        }
        var superAdmin = new User
        {
            Username = Soovioo.Api.Support.SupportController.BuiltInUsername,
            PasswordHash = "",
            Role = UserRole.SuperAdmin,
            SchoolId = null,
            MustChangePassword = true,
        };
        superAdmin.PasswordHash = hasher.HashPassword(superAdmin, seed.SuperAdminPassword);
        db.Users.Add(superAdmin);
        db.SaveChanges();
        return;
    }
    var demoUsers = new[]
    {
        new User { Username = Soovioo.Api.Support.SupportController.BuiltInUsername, PasswordHash = "", Role = UserRole.SuperAdmin, SchoolId = null },
        new User { Username = "admin", PasswordHash = "", Role = UserRole.Admin, SchoolId = demoSchoolId },
        new User { Username = "ucitel", PasswordHash = "", Role = UserRole.Teacher, SchoolId = demoSchoolId },
        new User { Username = "rodic", PasswordHash = "", Role = UserRole.Parent, SchoolId = demoSchoolId },
        new User { Username = "zak", PasswordHash = "", Role = UserRole.Student, SchoolId = demoSchoolId },
    };

    foreach (var user in demoUsers)
    {
        user.PasswordHash = hasher.HashPassword(user, $"{user.Username}123");
    }

    db.Users.AddRange(demoUsers);
    db.SaveChanges();

    // The humans behind the demo accounts (see People/Person.cs) — pupil
    // "zak" gets the demo student's own name.
    Person DemoPerson(string username, string firstName, string lastName)
    {
        var person = new Person
        {
            UserId = demoUsers.First(u => u.Username == username).Id,
            FirstName = firstName,
            LastName = lastName,
            SchoolId = demoSchoolId,
        };
        db.Persons.Add(person);
        return person;
    }
    DemoPerson("admin", "Alena", "Správcová");
    var teacherPerson = DemoPerson("ucitel", "Jana", "Učitelová");
    var parentPerson = DemoPerson("rodic", "Petr", "Novák");
    DemoPerson("zak", "Jan", "Novák");
    db.TeacherProfiles.Add(new TeacherProfile { Person = teacherPerson, SchoolId = demoSchoolId, Qualification = "M, Čj" });
    db.ParentProfiles.Add(new ParentProfile
    {
        Person = parentPerson,
        SchoolId = demoSchoolId,
        Children = [new ParentStudent { StudentId = 1, SchoolId = demoSchoolId }],
    });

    // Demo Student-role account logs in as the same demo student ("Jan
    // Novák", Id 1) the demo parent already sees — Student.UserId is a
    // direct FK (see Student.cs), settable here directly rather than via a
    // separate backfill: unlike TimetableEntry.TeacherId/Country/
    // MunicipalityCode elsewhere in this file, the Student row this points
    // at was already migrated in (HasData) before this function ever runs.
    var studentUser = demoUsers.First(u => u.Username == "zak");
    var demoStudent = db.Students.IgnoreQueryFilters().First(s => s.Id == 1);
    demoStudent.UserId = studentUser.Id;

    db.SaveChanges();
}

// TimetableEntry.HasData can't set TeacherId directly — "ucitel"'s Id isn't
// known until SeedUsers runs (no FK target yet at migration time, same
// reasoning as BackfillDemoStudentReferenceCodes below). Guarded by its own
// still-null check, not folded into SeedUsers's "any users exist" guard —
// that one short-circuits on every startup after the first, which would
// otherwise skip this backfill forever on a DB that already had users
// before this column existed. Scoped to the specific HasData-seeded demo
// entries (Id 1-4), not "any entry anywhere with no teacher" — a real
// school's intentionally-unassigned entries must never get silently
// reassigned by a startup seed step.
static void BackfillDemoTimetableTeacher(AppDbContext db)
{
    var teacher = db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Username == "ucitel");
    if (teacher is null)
    {
        return;
    }

    int[] demoTimetableEntryIds = [1, 2, 3, 4];
    var entries = db.TimetableEntries
        .IgnoreQueryFilters()
        .Where(e => demoTimetableEntryIds.Contains(e.Id) && e.TeacherId == null)
        .ToList();
    if (entries.Count == 0)
    {
        return;
    }

    foreach (var entry in entries)
    {
        entry.TeacherId = teacher.Id;
    }

    db.SaveChanges();
}

// Same reasoning as BackfillDemoTimetableTeacher: makes "ucitel" the class
// teacher of the HasData-seeded demo class 5.A (Id 1), which the restricted
// class-teacher Administrace view (and its e2e test) rely on. Only while
// still unset — never overrides a class teacher someone actually chose.
static void BackfillDemoClassTeacher(AppDbContext db)
{
    var teacher = db.Users.IgnoreQueryFilters().FirstOrDefault(u => u.Username == "ucitel" && u.SchoolId == 1);
    var demoClass = db.SchoolClasses.IgnoreQueryFilters().FirstOrDefault(c => c.Id == 1 && c.ClassTeacherId == null);
    if (teacher is null || demoClass is null)
    {
        return;
    }

    demoClass.ClassTeacherId = teacher.Id;
    db.SaveChanges();
}

// Countries/Districts/Municipalities are global matrika reference data
// (RAST/RAOR/RAUJ), extracted from MŠMT's own published codelist workbook —
// see ROADMAP.md for provenance. At 6400+ municipality rows, these go
// through a plain CSV-at-startup load rather than EF migration HasData
// (which would bloat the migration file enormously). Districts must be
// inserted before municipalities (FK), hence the separate SaveChanges calls.
// Persons came after the demo accounts: on a DB that already had them, the
// AddPersonsAndRoleProfiles migration names everyone after their username.
// Give the demo accounts the same real names a fresh seed (SeedUsers) uses —
// only while they still carry that placeholder, never over a real edit.
static void BackfillDemoPersonNames(AppDbContext db)
{
    (string Username, string FirstName, string LastName)[] demoNames =
    [
        ("admin", "Alena", "Správcová"),
        ("ucitel", "Jana", "Učitelová"),
        ("rodic", "Petr", "Novák"),
    ];
    foreach (var (username, firstName, lastName) in demoNames)
    {
        var person = db.Persons.IgnoreQueryFilters()
            .FirstOrDefault(p => p.SchoolId == 1 && p.User!.Username == username);
        if (person is not null && person.FirstName == "" && person.LastName == username)
        {
            person.FirstName = firstName;
            person.LastName = lastName;
        }
    }
    db.SaveChanges();
}

static void SeedReferenceData(AppDbContext db)
{
    if (db.Countries.Any())
    {
        return;
    }

    var seedDir = Path.Combine(AppContext.BaseDirectory, "Data", "Seed");

    db.Countries.AddRange(ReadCsvDataRows(Path.Combine(seedDir, "countries.csv"))
        .Select(r => new Country { Code = r[0], Name = r[1] }));
    db.SaveChanges();

    db.Districts.AddRange(ReadCsvDataRows(Path.Combine(seedDir, "districts.csv"))
        .Select(r => new District { Code = r[0], Name = NormalizeName(r[1]) }));
    db.SaveChanges();

    db.Municipalities.AddRange(ReadCsvDataRows(Path.Combine(seedDir, "municipalities.csv"))
        .Select(r => new Municipality { Code = r[0], Name = r[1], DistrictCode = r[2] }));
    db.SaveChanges();
}

// The demo student's municipality/country can't be set via migration-time
// HasData (see AppDbContext.cs comment) since the referenced rows don't
// exist until SeedReferenceData runs — so it's backfilled here instead,
// once, guarded by the field still being null.
static void BackfillDemoStudentReferenceCodes(AppDbContext db)
{
    var student = db.Students.IgnoreQueryFilters().FirstOrDefault(s => s.Id == 1 && s.MunicipalityCode == null);
    if (student is null)
    {
        return;
    }

    student.MunicipalityCode = "500054"; // RAUJ: Praha 1 (district CZ0100 follows from this)
    student.CountryCode = "203"; // RAST: Česká republika
    db.SaveChanges();
}

// Every account has a Person (name, contacts) — accounts created by code
// paths that didn't make one (early SuperAdmin creation) get one named
// after the username.
static void BackfillMissingPersons(AppDbContext db)
{
    var users = db.Users.IgnoreQueryFilters()
        .Where(u => !db.Persons.IgnoreQueryFilters().Any(p => p.UserId == u.Id))
        .ToList();
    foreach (var user in users)
    {
        db.Persons.Add(new Person { UserId = user.Id, LastName = user.Username, SchoolId = user.SchoolId });
    }
    if (users.Count > 0)
    {
        db.SaveChanges();
    }
}

// School bank accounts are stored as compact IBANs since the IBAN
// conversion (MySchoolController.SetBankAccount) — convert older values
// typed in the Czech format or with spaces; leave unconvertible ones as is.
static void BackfillSchoolIbans(AppDbContext db)
{
    var schools = db.Schools.Where(s => s.BankAccountNumber != null).ToList();
    var changed = false;
    foreach (var school in schools)
    {
        var (iban, _) = BankAccounts.ToIban(school.BankAccountNumber!);
        if (iban is not null && iban != school.BankAccountNumber)
        {
            school.BankAccountNumber = iban;
            changed = true;
        }
    }
    if (changed)
    {
        db.SaveChanges();
    }
}

static string NormalizeName(string name) => name.Replace('\n', ' ').Replace('\r', ' ').Trim();

// Minimal RFC4180-ish CSV reader: the source file has a couple of fields
// with embedded commas and one with an embedded newline (quoted), which a
// naive line-and-Split(',') read silently mangles — this handles quotes,
// "" escapes, and quoted embedded newlines correctly. Returns data rows
// only (header row skipped).
static List<string[]> ReadCsvDataRows(string path)
{
    var text = File.ReadAllText(path, Encoding.UTF8);
    var rows = new List<string[]>();
    var fields = new List<string>();
    var field = new StringBuilder();
    var inQuotes = false;

    void EndField()
    {
        fields.Add(field.ToString());
        field.Clear();
    }

    void EndRow()
    {
        EndField();
        rows.Add(fields.ToArray());
        fields.Clear();
    }

    var i = 0;
    while (i < text.Length)
    {
        var c = text[i];
        if (inQuotes)
        {
            if (c == '"')
            {
                if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i += 2;
                    continue;
                }

                inQuotes = false;
                i++;
                continue;
            }

            field.Append(c);
            i++;
            continue;
        }

        switch (c)
        {
            case '"':
                inQuotes = true;
                i++;
                break;
            case ',':
                EndField();
                i++;
                break;
            case '\r':
                i++;
                break;
            case '\n':
                EndRow();
                i++;
                break;
            default:
                field.Append(c);
                i++;
                break;
        }
    }

    if (field.Length > 0 || fields.Count > 0)
    {
        EndRow();
    }

    return rows.Skip(1).Where(r => r.Length > 1 && r.Any(f => f.Length > 0)).ToList();
}

/// <summary>Bound from the <c>Seed</c> config section — what
/// <c>SeedUsers</c> creates on an empty database.</summary>
class SeedOptions
{
    /// <summary>Demo accounts admin/ucitel/rodic/zak/superadmin with
    /// "&lt;username&gt;123" passwords. On only in appsettings.Development.json.</summary>
    public bool DemoAccounts { get; set; }

    /// <summary>First password of the built-in superadmin when
    /// <see cref="DemoAccounts"/> is off (Seed__SuperAdminPassword env var).</summary>
    public string? SuperAdminPassword { get; set; }
}
