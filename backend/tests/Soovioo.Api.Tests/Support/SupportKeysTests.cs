using System.Buffers.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Auth;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.People;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;
using Soovioo.Api.Tests.Backoffice;

namespace Soovioo.Api.Tests.Support;

/// <summary>The support enters only with a ticket's key the school's admin approved
/// (<c>authorized_keys</c>): keys made in the test play the backoffice.</summary>
public class SupportKeysTests : IDisposable
{
    private const int TicketId = 7;
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero));
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private School school = null!;
    private User admin = null!;
    private User teacher = null!;

    public void Dispose() => key.Dispose();

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
        new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private SupportKeys Keys(AppDbContext db) => AuthTestServices.SupportKeys(db, clock);

    private async Task SeedAsync(SupportAccess access = SupportAccess.Approval, SupportAsScope scope = SupportAsScope.Users)
    {
        using var db = Db();
        school = new School { Name = "ZŠ Ukázkov", Slug = "ukazkov", SupportAccess = access, SupportAs = scope };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        db.Users.Add(new User { Username = SupportController.BuiltInUsername, PasswordHash = "x", IsSuperAdmin = true });
        await db.SaveChangesAsync();
        User Account(string username, string first, string last, UserRole role)
        {
            var user = new User
            {
                Username = username, PasswordHash = "x", SchoolId = school.Id,
                Roles = [school.Roles.Single(r => r.BuiltIn == role)],
                Person = new Person { FirstName = first, LastName = last, SchoolId = school.Id },
            };
            user.ActiveRole = user.Roles[0];
            db.Users.Add(user);
            return user;
        }
        admin = Account("reditel", "Řehoř", "Ředitel", UserRole.Admin);
        teacher = Account("novak", "Jan", "Novák", UserRole.Teacher);
        await db.SaveChangesAsync();
    }

    private static string Fingerprint(ECDsa ecdsa) => SupportKeys.Fingerprint(ecdsa.ExportSubjectPublicKeyInfo());

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, JsonSerializerOptions.Web);

    /// <summary>The backoffice's <c>RequestSupportAccess</c>.</summary>
    private async Task<CommandAnswer> RequestAsync(ECDsa? with = null, int ticketId = TicketId)
    {
        with ??= key;
        using var db = Db();
        return await Keys(db).ReceiveAsync(Json(new
        {
            ticketId, schoolId = school.Id, ticketTitle = "Nejde tisk", ticketAuthorUserId = teacher.Id,
            publicKey = Convert.ToBase64String(with.ExportSubjectPublicKeyInfo()), fingerprint = Fingerprint(with),
            operatorId = 4, operatorName = "Jana Operátorka", requestedAs = new[] { new { userId = teacher.Id, name = "Jan Novák" } },
            reason = "Potřebuji vidět tisk.",
        }));
    }

    private async Task<string?> ApproveAsync(DateTimeOffset? until = null, params int[] users)
    {
        using var db = Db();
        var stored = await db.SupportAccessKeys.OrderBy(k => k.Id).LastAsync();
        var me = await db.Users.Include(u => u.Person).FirstAsync(u => u.Id == admin.Id);
        var error = await Keys(db).ApproveAsync(stored, await db.Schools.FirstAsync(), me, "Řehoř Ředitel",
            until ?? clock.GetUtcNow().AddHours(2), users.Length == 0 ? [teacher.Id] : users);
        await db.SaveChangesAsync();
        return error;
    }

    /// <summary>The backoffice's <c>SupportChallenge</c>: the challenge, or null when refused.</summary>
    private async Task<(string? Challenge, string? EnterUrl, string Json)> ChallengeAsync(int? asUserId, string? fingerprint = null)
    {
        using var db = Db();
        var answer = await Keys(db).ChallengeAsync(Json(new
        {
            ticketId = TicketId, schoolId = school.Id, fingerprint = fingerprint ?? Fingerprint(key), asUserId,
        }));
        if (answer.StatusCode != StatusCodes.Status200OK)
        {
            return (null, null, JsonDocument.Parse(answer.Json!).RootElement.GetProperty("error").GetString()!);
        }
        var result = JsonDocument.Parse(answer.Json!).RootElement;
        return (result.GetProperty("challenge").GetString(), result.GetProperty("enterUrl").GetString(), answer.Json!);
    }

    /// <summary>What the backoffice signs, signed with <paramref name="with"/>.</summary>
    private string Entry(string challenge, int? asUserId, ECDsa? with = null, int ticketId = TicketId, Guid? instanceId = null)
    {
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 1, challenge, instanceId = instanceId ?? FakeBackoffice.InstanceId, schoolId = school.Id, ticketId,
            fingerprint = Fingerprint(key), asUserId, operatorId = 4, operatorName = "Jana Operátorka",
            issuedAt = clock.GetUtcNow(),
        }, JsonSerializerOptions.Web));
        var signature = (with ?? key).SignData(Encoding.ASCII.GetBytes(payload), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{payload}.{Base64Url.EncodeToString(signature)}";
    }

    /// <summary>Anonymous, as the entry page posts it: no school context yet.</summary>
    private async Task<LoginResponse?> EnterAsync(string entry)
    {
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeCurrentSchoolContext(null));
        return await Keys(db).EnterAsync(new DefaultHttpContext(), entry);
    }

    private async Task<LoginResponse?> ApprovedAndEnteredAsync()
    {
        await RequestAsync();
        Assert.Null(await ApproveAsync());
        var (challenge, _, _) = await ChallengeAsync(teacher.Id);
        return await EnterAsync(Entry(challenge!, teacher.Id));
    }

    [Fact]
    public async Task With_the_key_the_school_approved_the_support_enters_as_whom_it_named()
    {
        await SeedAsync();
        Assert.Equal(StatusCodes.Status200OK, (await RequestAsync()).StatusCode);
        using (var db = Db())
        {
            var stored = Assert.Single(db.SupportAccessKeys);
            Assert.Equal(SupportAccessKeyStatus.Pending, stored.Status);
            Assert.Equal("Jana Operátorka", stored.OperatorName);
            Assert.Equal([teacher.Id], stored.RequestedAsUserIds);
        }
        // Pending: no challenge.
        Assert.Null((await ChallengeAsync(teacher.Id)).Challenge);

        Assert.Null(await ApproveAsync());
        var (challenge, enterUrl, _) = await ChallengeAsync(teacher.Id);
        Assert.Equal("http://localhost:4200/ukazkov/support/enter", enterUrl);

        var login = await EnterAsync(Entry(challenge!, teacher.Id));

        Assert.NotNull(login);
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(login!.Token).Claims.ToList();
        Assert.Contains(claims, c => c.Type == SupportClaims.By && c.Value == "Jana Operátorka");
        Assert.Contains(claims, c => c.Type == SupportClaims.AsUserId && c.Value == teacher.Id.ToString());
        using var after = Db();
        var session = Assert.Single(after.SupportSessions);
        Assert.Equal(TicketId, session.TicketId);
        Assert.Equal(4, session.OperatorId);
        Assert.Equal("Jana Operátorka", session.SuperAdminUsername);
        Assert.Empty(after.SupportEntryChallenges);
        Assert.Contains(after.OutboxEvents, e => e.Type == SupportKeys.SessionStartedEvent);
        Assert.Contains(after.OutboxEvents, e => e.Type == SupportKeys.DecidedEvent && e.Payload.Contains("\"Approved\""));
    }

    [Fact]
    public async Task A_challenge_works_once()
    {
        await SeedAsync();
        await RequestAsync();
        await ApproveAsync();
        var (challenge, _, _) = await ChallengeAsync(teacher.Id);
        Assert.NotNull(await EnterAsync(Entry(challenge!, teacher.Id)));
        Assert.Null(await EnterAsync(Entry(challenge!, teacher.Id)));
    }

    [Fact]
    public async Task A_challenge_runs_out()
    {
        await SeedAsync();
        await RequestAsync();
        await ApproveAsync();
        var (challenge, _, _) = await ChallengeAsync(teacher.Id);
        clock.Now += SupportKeys.ChallengeLifetime + SupportKeys.ClockSkew + TimeSpan.FromSeconds(1);
        Assert.Null(await EnterAsync(Entry(challenge!, teacher.Id)));
    }

    [Fact]
    public async Task Another_key_another_ticket_another_instance_or_someone_not_approved_does_not_get_in()
    {
        await SeedAsync();
        await RequestAsync();
        await ApproveAsync();
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var (c1, _, _) = await ChallengeAsync(teacher.Id);
        Assert.Null(await EnterAsync(Entry(c1!, teacher.Id, with: stranger)));
        var (c2, _, _) = await ChallengeAsync(teacher.Id);
        Assert.Null(await EnterAsync(Entry(c2!, teacher.Id, ticketId: TicketId + 1)));
        var (c3, _, _) = await ChallengeAsync(teacher.Id);
        Assert.Null(await EnterAsync(Entry(c3!, teacher.Id, instanceId: Guid.NewGuid())));
        // As someone the challenge wasn't for, or the school didn't approve.
        var (c4, _, _) = await ChallengeAsync(teacher.Id);
        Assert.Null(await EnterAsync(Entry(c4!, admin.Id)));
        Assert.Contains("neschválila", (await ChallengeAsync(admin.Id)).Json);
        // A key of another ticket isn't this one's.
        Assert.Contains("neschválila", (await ChallengeAsync(teacher.Id, Fingerprint(stranger))).Json);
        Assert.Null(await EnterAsync("nesmysl"));
        using var db = Db();
        Assert.Empty(db.SupportSessions);
    }

    [Fact]
    public async Task Revoking_stops_entering_and_ends_the_running_sessions()
    {
        await SeedAsync();
        Assert.NotNull(await ApprovedAndEnteredAsync());
        var (challenge, _, _) = await ChallengeAsync(teacher.Id);

        using (var db = Db())
        {
            var stored = await db.SupportAccessKeys.SingleAsync();
            var me = await db.Users.FirstAsync(u => u.Id == admin.Id);
            Assert.Null(await Keys(db).RevokeAsync(stored, me, "Řehoř Ředitel"));
            await db.SaveChangesAsync();
        }

        Assert.Null(await EnterAsync(Entry(challenge!, teacher.Id)));
        using var after = Db();
        var session = Assert.Single(after.SupportSessions);
        Assert.NotNull(session.EndedAt);
        Assert.Equal(SupportKeys.RevokedReason, session.EndReason);
        Assert.Contains(after.OutboxEvents, e => e.Type == SupportKeys.DecidedEvent && e.Payload.Contains("\"Revoked\""));
    }

    [Fact]
    public async Task A_rejected_or_run_out_approval_lets_nobody_in()
    {
        await SeedAsync();
        await RequestAsync();
        using (var db = Db())
        {
            var me = await db.Users.FirstAsync(u => u.Id == admin.Id);
            Assert.Null(Keys(db).Reject(await db.SupportAccessKeys.SingleAsync(), me, "Řehoř Ředitel", "Teď ne."));
            await db.SaveChangesAsync();
        }
        Assert.Null((await ChallengeAsync(teacher.Id)).Challenge);
        Assert.Equal("O tomto klíči už je rozhodnuto.", await ApproveAsync());

        using var second = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await RequestAsync(second);
        Assert.Null(await ApproveAsync(clock.GetUtcNow().AddHours(1)));
        clock.Now += TimeSpan.FromHours(2);
        Assert.Null((await ChallengeAsync(teacher.Id, Fingerprint(second))).Challenge);
    }

    [Fact]
    public async Task A_school_that_never_lets_the_support_in_rejects_at_once_one_that_always_does_approves()
    {
        await SeedAsync(SupportAccess.Never);
        await RequestAsync();
        using (var db = Db())
        {
            Assert.Equal(SupportAccessKeyStatus.Rejected, (await db.SupportAccessKeys.SingleAsync()).Status);
        }

        await SeedAsync(SupportAccess.Always);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using (var db = Db())
        {
            var answer = await Keys(db).ReceiveAsync(Json(new
            {
                ticketId = 9, schoolId = school.Id, ticketTitle = "Rozvrh",
                publicKey = Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), fingerprint = Fingerprint(other),
                operatorId = 4, operatorName = "Jana Operátorka", requestedAs = new[] { new { userId = teacher.Id, name = "Jan Novák" } },
            }));
            Assert.Equal(StatusCodes.Status200OK, answer.StatusCode);
        }
        using var after = Db();
        var approved = await after.SupportAccessKeys.SingleAsync(k => k.SchoolId == school.Id);
        Assert.Equal(SupportAccessKeyStatus.Approved, approved.Status);
        Assert.Equal([teacher.Id], approved.ApprovedAsUserIds);
    }

    [Fact]
    public async Task The_ticket_ending_expires_its_key_and_ends_its_sessions()
    {
        await SeedAsync();
        Assert.NotNull(await ApprovedAndEnteredAsync());
        using (var db = Db())
        {
            await Keys(db).ExpireAsync(school.Id, TicketId);
            await db.SaveChangesAsync();
        }
        using var after = Db();
        Assert.Equal(SupportAccessKeyStatus.Expired, (await after.SupportAccessKeys.SingleAsync()).Status);
        Assert.NotNull((await after.SupportSessions.SingleAsync()).EndedAt);
        Assert.Null((await ChallengeAsync(teacher.Id)).Challenge);
    }

    [Fact]
    public async Task The_backoffice_ends_the_tickets_sessions()
    {
        await SeedAsync();
        Assert.NotNull(await ApprovedAndEnteredAsync());
        using (var db = Db())
        {
            await Keys(db).EndAsync(Json(new { ticketId = TicketId, schoolId = school.Id }));
        }
        using var after = Db();
        Assert.NotNull((await after.SupportSessions.SingleAsync()).EndedAt);
    }

    [Fact]
    public async Task A_key_that_is_not_the_fingerprints_is_refused()
    {
        await SeedAsync();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var db = Db();
        var answer = await Keys(db).ReceiveAsync(Json(new
        {
            ticketId = TicketId, schoolId = school.Id, ticketTitle = "Rozvrh",
            publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()), fingerprint = Fingerprint(other),
            operatorId = 4, operatorName = "Jana Operátorka",
        }));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, answer.StatusCode);
    }
}
