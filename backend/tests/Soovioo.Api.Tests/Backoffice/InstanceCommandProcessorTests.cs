using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Soovioo.Api.Auth;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Support;
using Soovioo.Api.Tests.Auth;

namespace Soovioo.Api.Tests.Backoffice;

public class InstanceCommandProcessorTests
{
    private readonly string dbName = Guid.NewGuid().ToString();
    private readonly FakeClock clock = new(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero));
    private readonly ECDsaSecurityKey commandsKey = NewKey();
    private readonly FakeBackoffice backoffice = new();
    private readonly Jwks jwks;
    private readonly IMemoryCache cache = new MemoryCache(new MemoryCacheOptions());

    public InstanceCommandProcessorTests() => jwks = new Jwks(commandsKey);

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options,
            new FakeCurrentSchoolContext(null));

    private SupportPlanClient Plans() =>
        new(backoffice, Microsoft.Extensions.Options.Options.Create(new SupportPlanOptions()), cache,
            NullLogger<SupportPlanClient>.Instance);

    private InstanceCommandProcessor Processor(AppDbContext db, BackofficeKeys? keys = null) =>
        new(db, backoffice, keys ?? Keys(), Plans(), clock, NullLogger<InstanceCommandProcessor>.Instance,
            new Soovioo.Api.Demo.DemoSchoolPurger(db, null!));

    private BackofficeKeys Keys() => new(new HttpClient(jwks), backoffice, clock, NullLogger<BackofficeKeys>.Instance);

    private static ECDsaSecurityKey NewKey()
    {
        var key = new ECDsaSecurityKey(ECDsa.Create(ECCurve.NamedCurves.nistP256));
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());
        return key;
    }

    /// <summary>What the backoffice sends with a command.</summary>
    private string Bearer(Guid commandId, Guid? audience = null, ECDsaSecurityKey? key = null, TimeSpan? age = null)
    {
        var issued = (clock.Now - (age ?? TimeSpan.Zero)).UtcDateTime;
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "soovioo-backoffice",
            Audience = (audience ?? FakeBackoffice.InstanceId).ToString(),
            Subject = new ClaimsIdentity([new Claim("sub", "backoffice"), new Claim("command_id", commandId.ToString())]),
            IssuedAt = issued,
            NotBefore = issued,
            Expires = issued.AddMinutes(1),
            SigningCredentials = new SigningCredentials(key ?? commandsKey, SecurityAlgorithms.EcdsaSha256),
        });
        return $"Bearer {token}";
    }

    private static string Command(string type, object payload) =>
        JsonSerializer.Serialize(new { type, payload }, JsonSerializerOptions.Web);

    private async Task<CommandAnswer> Send(Guid id, string type, object payload, string? bearer = null)
    {
        await using var db = Db();
        return await Processor(db).ProcessAsync(id, bearer ?? Bearer(id), Command(type, payload));
    }

    private async Task<School> SeedSchool(bool withUser = false, bool isDemo = false)
    {
        await using var db = Db();
        var school = new School { Name = "ZŠ Lesní", Slug = "zs-lesni", Izo = "102345678", IsDemo = isDemo };
        RoleSeeder.AddMissing(school);
        db.Schools.Add(school);
        await db.SaveChangesAsync();
        if (withUser)
        {
            db.Users.Add(new User { Username = "admin", PasswordHash = "", SchoolId = school.Id });
            await db.SaveChangesAsync();
        }
        return school;
    }

    [Fact]
    public async Task UpdateSchoolSetsTheNameAndIdsTheOperatorCorrected()
    {
        var school = await SeedSchool();

        var answer = await Send(Guid.NewGuid(), "UpdateSchool", new { schoolId = school.Id, name = " ZŠ Polní ", izo = "", redIzo = "600012345" });

        Assert.Equal(StatusCodes.Status200OK, answer.StatusCode);
        await using var db = Db();
        var updated = await db.Schools.SingleAsync();
        Assert.Equal("ZŠ Polní", updated.Name);
        Assert.Null(updated.Izo);
        Assert.Equal("600012345", updated.RedIzo);
    }

    [Fact]
    public async Task TheSameCommandIsCarriedOutOnceAndAnsweredTheSame()
    {
        var school = await SeedSchool();
        var id = Guid.NewGuid();
        var first = await Send(id, "UpdateSchool", new { schoolId = school.Id, name = "První" });

        await using (var db = Db())
        {
            (await db.Schools.SingleAsync()).Name = "Změněno mezitím";
            await db.SaveChangesAsync();
        }
        var again = await Send(id, "UpdateSchool", new { schoolId = school.Id, name = "První" });

        Assert.Equal(first, again);
        await using var check = Db();
        Assert.Equal("Změněno mezitím", (await check.Schools.SingleAsync()).Name);
        Assert.Single(await check.ProcessedCommands.ToListAsync());
    }

    [Fact]
    public async Task DeleteSchoolDeletesOnlyASchoolWithoutUsersAndAGoneOneCountsAsDeleted()
    {
        var withUsers = await SeedSchool(withUser: true);

        var refused = await Send(Guid.NewGuid(), "DeleteSchool", new { schoolId = withUsers.Id });
        var gone = await Send(Guid.NewGuid(), "DeleteSchool", new { schoolId = 999 });

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, refused.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, gone.StatusCode);
        await using var db = Db();
        Assert.Single(await db.Schools.ToListAsync());
    }

    [Fact]
    public async Task DeleteSchoolDeletesAnEmptySchool()
    {
        var school = await SeedSchool();

        var answer = await Send(Guid.NewGuid(), "DeleteSchool", new { schoolId = school.Id });

        Assert.Equal(StatusCodes.Status200OK, answer.StatusCode);
        await using var db = Db();
        Assert.Empty(await db.Schools.ToListAsync());
    }

    [Fact]
    public async Task SchoolPlanChangedForgetsThePlanKeptFromBefore()
    {
        backoffice.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"code":"Priority","name":"Prioritní","responseWorkingDays":1}""", System.Text.Encoding.UTF8, "application/json"),
        };
        var school = new School { Id = 7, Name = "Š", Slug = "s" };
        await Plans().GetPlanAsync(school);
        backoffice.Respond = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

        var answer = await Send(Guid.NewGuid(), "SchoolPlanChanged", new { schoolId = 7 });

        Assert.Equal(StatusCodes.Status200OK, answer.StatusCode);
        Assert.Equal(new SupportPlanOptions().Default, await Plans().GetPlanAsync(school));
    }

    [Fact]
    public async Task AnUnknownCommandOrBadPayloadIsRefused()
    {
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, (await Send(Guid.NewGuid(), "CreateRocket", new { })).StatusCode);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, (await Send(Guid.NewGuid(), "UpdateSchool", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task OnlyATokenOfTheBackofficeForThisInstanceAndCommandIsTaken()
    {
        var school = await SeedSchool();
        var id = Guid.NewGuid();
        var payload = new { schoolId = school.Id, name = "Podvrženo" };

        Assert.Equal(401, (await Send(id, "UpdateSchool", payload, bearer: "")).StatusCode);
        Assert.Equal(401, (await Send(id, "UpdateSchool", payload, bearer: Bearer(Guid.NewGuid()))).StatusCode);
        Assert.Equal(401, (await Send(id, "UpdateSchool", payload, bearer: Bearer(id, audience: Guid.NewGuid()))).StatusCode);
        Assert.Equal(401, (await Send(id, "UpdateSchool", payload, bearer: Bearer(id, key: NewKey()))).StatusCode);
        Assert.Equal(401, (await Send(id, "UpdateSchool", payload, bearer: Bearer(id, age: TimeSpan.FromMinutes(2)))).StatusCode);

        await using var db = Db();
        Assert.Equal("ZŠ Lesní", (await db.Schools.SingleAsync()).Name);
        Assert.Empty(await db.ProcessedCommands.ToListAsync());
    }

    [Fact]
    public async Task ANewKeyOfTheBackofficeIsFetchedWhenACommandIsSignedWithIt()
    {
        var school = await SeedSchool();
        var keys = Keys();
        await keys.GetAsync(null);
        var newKey = NewKey();
        jwks.Keys = [newKey];
        clock.Now += TimeSpan.FromMinutes(2);
        var id = Guid.NewGuid();

        await using var db = Db();
        var answer = await Processor(db, keys).ProcessAsync(
            id, Bearer(id, key: newKey), Command("UpdateSchool", new { schoolId = school.Id, name = "Nový klíč" }));

        Assert.Equal(StatusCodes.Status200OK, answer.StatusCode);
        Assert.Equal(2, jwks.Requests);
    }

    [Fact]
    public async Task ANotConnectedInstanceHasNoCommands()
    {
        await using var db = Db();
        var processor = new InstanceCommandProcessor(db, FakeBackoffice.NotConnected(), Keys(), Plans(), clock,
            NullLogger<InstanceCommandProcessor>.Instance);
        var id = Guid.NewGuid();

        Assert.Equal(404, (await processor.ProcessAsync(id, Bearer(id), Command("SchoolPlanChanged", new { schoolId = 1 }))).StatusCode);
    }

    [Theory]
    [InlineData(8081, 8081, "/internal/v1/commands/x", true)]
    [InlineData(8081, 8081, "/api/schools", false)]
    [InlineData(8080, 8081, "/internal/v1/commands/x", false)]
    [InlineData(8080, 8081, "/api/schools", true)]
    [InlineData(8080, null, "/internal/v1/commands/x", false)]
    [InlineData(8080, null, "/api/schools", true)]
    public void TheInternalPortServesOnlyTheInternalInterfaceAndOnlyIt(int localPort, int? internalPort, string path, bool allowed)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalPort = localPort;
        context.Request.Path = path;

        Assert.Equal(allowed, InternalApi.IsAllowed(context, internalPort));
    }

    /// <summary>The backoffice's <c>/.well-known/jwks.json</c>.</summary>
    private class Jwks(ECDsaSecurityKey key) : HttpMessageHandler
    {
        public ECDsaSecurityKey[] Keys { get; set; } = [key];
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var keys = Keys.Select(k =>
            {
                var p = k.ECDsa.ExportParameters(false);
                return new { kty = "EC", crv = "P-256", x = Base64UrlEncoder.Encode(p.Q.X), y = Base64UrlEncoder.Encode(p.Q.Y), kid = k.KeyId, use = "sig", alg = "ES256" };
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { keys }), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}
