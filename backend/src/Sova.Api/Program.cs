using System.Text;
using System.Text.Json.Serialization;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using Sova.Api.Auth;
using Sova.Api.Data;
using Sova.Api.Evidence;
using Sova.Api.Matrika;

QuestPDF.Settings.License = LicenseType.Community;

// Windows-1250 is required (not .NET's default) for the matrika XML export —
// it's not included by default outside .NET Framework.
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSchoolContext, HttpCurrentSchoolContext>();
builder.Services.AddScoped<MatrikaExportService>();

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
        };
    });
builder.Services.AddAuthorization();

// Dev: any origin, since the dev server is also reached from a phone on the
// LAN by IP, not just http://localhost:4200. No cookies/credentials are used
// (Bearer tokens only), so this is safe to keep broad — but ONLY while
// actually running in Development (gated below), so a deployment that
// forgets to set ASPNETCORE_ENVIRONMENT=Production doesn't silently inherit
// an open CORS policy.
const string devCorsPolicy = "DevClients";
builder.Services.AddCors(options =>
    options.AddPolicy(devCorsPolicy, policy =>
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()));

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
            .AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedUsers(db);
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

app.MapControllers();

app.MapHangfireDashboard();

app.Run();

static void SeedUsers(AppDbContext db)
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
    var demoUsers = new[]
    {
        new User { Username = "superadmin", PasswordHash = "", Role = UserRole.SuperAdmin, SchoolId = null },
        new User { Username = "admin", PasswordHash = "", Role = UserRole.Admin, SchoolId = demoSchoolId },
        new User { Username = "ucitel", PasswordHash = "", Role = UserRole.Teacher, SchoolId = demoSchoolId },
        new User { Username = "rodic", PasswordHash = "", Role = UserRole.Parent, SchoolId = demoSchoolId },
    };

    foreach (var user in demoUsers)
    {
        user.PasswordHash = hasher.HashPassword(user, $"{user.Username}123");
    }

    db.Users.AddRange(demoUsers);
    db.SaveChanges();

    var parent = demoUsers.First(u => u.Username == "rodic");
    db.ParentStudents.Add(new ParentStudent { UserId = parent.Id, StudentId = 1, SchoolId = demoSchoolId });
    db.SaveChanges();
}
