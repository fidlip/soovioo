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

QuestPDF.Settings.License = LicenseType.Community;

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

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddHangfire(config =>
    config.UsePostgreSqlStorage(c => c.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing Jwt configuration section.");
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

const string devCorsPolicy = "DevClients";
builder.Services.AddCors(options =>
    options.AddPolicy(devCorsPolicy, policy =>
        // Dev-only: any origin, since the dev server can be reached from a
        // phone on the LAN by IP, not just http://localhost:4200. No
        // cookies/credentials are used (Bearer tokens only), so this is safe
        // to keep broad here. Tighten before any real deployment.
        policy.AllowAnyOrigin()
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

app.UseCors(devCorsPolicy);

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
