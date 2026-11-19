using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Backoffice;

/// <summary>A command from the backoffice carried out, with what it answered —
/// the same command again gets the same answer and isn't carried out twice.</summary>
public class ProcessedCommand
{
    public Guid Id { get; set; }
    public required string Type { get; set; }
    public int StatusCode { get; set; }

    /// <summary>JSON.</summary>
    public string? Result { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}

/// <summary>What the internal interface answers the backoffice.</summary>
public record CommandAnswer(int StatusCode, string? Json = null)
{
    public static CommandAnswer Ok(object result) =>
        new(StatusCodes.Status200OK, JsonSerializer.Serialize(result, JsonSerializerOptions.Web));

    /// <summary>The command can't be carried out; the backoffice won't send it again.</summary>
    public static CommandAnswer Refused(string reason) =>
        new(StatusCodes.Status422UnprocessableEntity, JsonSerializer.Serialize(new { error = reason }, JsonSerializerOptions.Web));
}

/// <summary>Commands from the backoffice (<c>POST /internal/v1/commands/{id}</c>):
/// checks the backoffice's token for this command, carries it out once.</summary>
public class InstanceCommandProcessor(
    AppDbContext db,
    IBackofficeClient backoffice,
    BackofficeKeys keys,
    Support.SupportPlanClient plans,
    TimeProvider clock,
    ILogger<InstanceCommandProcessor> logger,
    Demo.DemoSchoolPurger? demoPurger = null)
{
    public const string Issuer = "soovioo-backoffice";
    public const string CommandIdClaim = "command_id";

    public async Task<CommandAnswer> ProcessAsync(Guid id, string? authorization, string body, CancellationToken cancellationToken = default)
    {
        if (backoffice.Connection is not { } connection)
        {
            return new CommandAnswer(StatusCodes.Status404NotFound);
        }
        if (!await IsFromBackofficeAsync(id, authorization, connection, cancellationToken))
        {
            return new CommandAnswer(StatusCodes.Status401Unauthorized);
        }

        string type;
        JsonElement payload;
        try
        {
            using var document = JsonDocument.Parse(body);
            type = document.RootElement.GetProperty("type").GetString() ?? "";
            payload = document.RootElement.GetProperty("payload").Clone();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new CommandAnswer(StatusCodes.Status400BadRequest);
        }

        if (await db.ProcessedCommands.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken) is { } done)
        {
            return new CommandAnswer(done.StatusCode, done.Result);
        }

        CommandAnswer answer;
        try
        {
            answer = await CarryOutAsync(type, payload);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            answer = CommandAnswer.Refused($"Invalid payload: {ex.Message}");
        }
        logger.LogInformation("Command {CommandId} ({Type}) from the backoffice: {Status}", id, type, answer.StatusCode);

        db.ChangeTracker.Clear();
        db.ProcessedCommands.Add(new ProcessedCommand
        {
            Id = id,
            Type = type.Length > 64 ? type[..64] : type,
            StatusCode = answer.StatusCode,
            Result = answer.Json,
            ProcessedAt = clock.GetUtcNow(),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The same command arrived twice at once.
            if (!await db.ProcessedCommands.AsNoTracking().AnyAsync(c => c.Id == id, cancellationToken))
            {
                throw;
            }
        }
        return answer;
    }

    private Task<CommandAnswer> CarryOutAsync(string type, JsonElement payload) => type switch
    {
        "UpdateSchool" => UpdateSchoolAsync(payload),
        "DeleteSchool" => DeleteSchoolAsync(payload),
        "SchoolPlanChanged" => Task.FromResult(SchoolPlanChanged(payload)),
        _ => Task.FromResult(CommandAnswer.Refused($"Unknown command {type}.")),
    };

    /// <summary>Name, IZO and RED_IZO as the operator corrected them; an empty
    /// IZO or RED_IZO clears it.</summary>
    private async Task<CommandAnswer> UpdateSchoolAsync(JsonElement payload)
    {
        var schoolId = payload.GetProperty("schoolId").GetInt32();
        var school = await db.Schools.FindAsync(schoolId);
        if (school is null)
        {
            return CommandAnswer.Refused($"School {schoolId} doesn't exist.");
        }
        if (payload.TryGetProperty("name", out var name))
        {
            var value = name.GetString()?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return CommandAnswer.Refused("The name can't be empty.");
            }
            school.Name = value;
        }
        if (payload.TryGetProperty("izo", out var izo))
        {
            school.Izo = NullIfEmpty(izo.GetString());
        }
        if (payload.TryGetProperty("redIzo", out var redIzo))
        {
            school.RedIzo = NullIfEmpty(redIzo.GetString());
        }
        await db.SaveChangesAsync();
        return CommandAnswer.Ok(new { schoolId });
    }

    /// <summary>As <see cref="SchoolsController.DeleteSchool"/>; a school that's
    /// already gone counts as deleted.</summary>
    private async Task<CommandAnswer> DeleteSchoolAsync(JsonElement payload)
    {
        var schoolId = payload.GetProperty("schoolId").GetInt32();
        return await SchoolDeletion.DeleteAsync(db, schoolId, demoPurger) switch
        {
            SchoolDeletion.Result.HasUsers => CommandAnswer.Refused(SchoolDeletion.HasUsersMessage),
            _ => CommandAnswer.Ok(new { schoolId }),
        };
    }

    private CommandAnswer SchoolPlanChanged(JsonElement payload)
    {
        var schoolId = payload.GetProperty("schoolId").GetInt32();
        plans.Forget(schoolId);
        return CommandAnswer.Ok(new { schoolId });
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Signed with the backoffice's commands key, for this instance
    /// (<c>aud</c>) and this command (<c>command_id</c>), not expired.</summary>
    private async Task<bool> IsFromBackofficeAsync(
        Guid id, string? authorization, BackofficeConnection connection, CancellationToken cancellationToken)
    {
        if (authorization is null || !authorization.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return false;
        }
        var token = authorization["Bearer ".Length..].Trim();
        var handler = new JsonWebTokenHandler();
        string? keyId;
        try
        {
            keyId = handler.ReadJsonWebToken(token).Kid;
        }
        catch (ArgumentException)
        {
            return false;
        }
        var now = clock.GetUtcNow().UtcDateTime;
        var skew = TimeSpan.FromSeconds(30);
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = connection.InstanceId.ToString(),
            IssuerSigningKeys = await keys.GetAsync(keyId, cancellationToken),
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireExpirationTime = true,
            LifetimeValidator = (notBefore, expires, _, _) =>
                expires > now - skew && (notBefore is null || notBefore <= now + skew),
        });
        if (!result.IsValid)
        {
            logger.LogWarning(result.Exception, "A command {CommandId} with a token the backoffice didn't issue for it.", id);
            return false;
        }
        return result.Claims.TryGetValue(CommandIdClaim, out var commandId) && commandId as string == id.ToString();
    }
}

/// <summary>The internal interface for the backoffice, on its own port
/// (<see cref="BackofficeOptions.InternalPort"/>), which isn't published
/// outside: the public port has no <c>/internal</c>, the internal one nothing
/// else.</summary>
public static class InternalApi
{
    public const string PathPrefix = "/internal";

    public static void UseInternalPortSeparation(this WebApplication app)
    {
        var internalPort = app.Services.GetRequiredService<IOptions<BackofficeOptions>>().Value.InternalPort;
        app.Use(async (context, next) =>
        {
            if (IsAllowed(context, internalPort))
            {
                await next(context);
                return;
            }
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }

    public static bool IsAllowed(HttpContext context, int? internalPort)
    {
        var onInternalPort = internalPort is not null && context.Connection.LocalPort == internalPort;
        return onInternalPort == context.Request.Path.StartsWithSegments(PathPrefix);
    }

    public static void MapInternalApi(this WebApplication app)
    {
        app.MapPost($"{PathPrefix}/v1/commands/{{id:guid}}", async (
            Guid id, HttpRequest request, InstanceCommandProcessor processor, CancellationToken cancellationToken) =>
        {
            using var reader = new StreamReader(request.Body);
            var answer = await processor.ProcessAsync(
                id, request.Headers.Authorization.ToString() is { Length: > 0 } auth ? auth : null,
                await reader.ReadToEndAsync(cancellationToken), cancellationToken);
            return answer.Json is null
                ? Results.StatusCode(answer.StatusCode)
                : Results.Content(answer.Json, "application/json", statusCode: answer.StatusCode);
        }).AllowAnonymous().WithMetadata(new Audit.NoAuditAttribute());
    }
}
