using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Soovioo.Api.Auth;
using Soovioo.Api.Backoffice;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;

namespace Soovioo.Api.Support;

/// <summary>The support enters only with a ticket's key the school's admin approved
/// (<see cref="SupportAccessKey"/>): the backoffice asks with the public key
/// (<see cref="RequestCommand"/>), the admin approves or rejects it and may revoke it, entering
/// is the instance's one-time challenge (<see cref="ChallengeCommand"/>) signed with the private
/// key, which only the backoffice has.</summary>
public class SupportKeys(
    AppDbContext db,
    IBackofficeClient backoffice,
    SupportSessionService sessions,
    IOptions<SupportOptions> options,
    IOptions<AppLinkOptions> links,
    TimeProvider clock)
{
    public const string RequestCommand = "RequestSupportAccess";
    public const string ChallengeCommand = "SupportChallenge";
    public const string EndCommand = "EndSupportSession";

    /// <summary>Events for the backoffice.</summary>
    public const string DecidedEvent = "SupportAccessDecided";
    public const string SessionStartedEvent = "SupportSessionStarted";

    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);

    /// <summary>Clocks of the backoffice and the instance may differ this much.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public const int MaxReasonLength = 2000;
    public const string RevokedReason = "Škola odvolala klíč pro vstup podpory.";
    public const string Invalid = "Odkaz pro vstup do školy je neplatný nebo vypršel. Zkuste to znovu z backoffice.";

    // ---- Commands from the backoffice ----

    /// <summary>The support asks to enter for a ticket with its public key. A school that never
    /// lets the support in rejects it at once; one that lets it in any time
    /// (<see cref="SupportAccess.Always"/>) approves it at once as the ticket asks — the key is
    /// still the school's to revoke.</summary>
    public async Task<CommandAnswer> ReceiveAsync(JsonElement payload)
    {
        var request = payload.Deserialize<AccessRequest>(JsonSerializerOptions.Web)
            ?? throw new JsonException("No payload.");
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == request.SchoolId);
        if (school is null)
        {
            return CommandAnswer.Refused($"School {request.SchoolId} doesn't exist.");
        }
        byte[] publicKey;
        try
        {
            publicKey = Convert.FromBase64String(request.PublicKey);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            if (ecdsa.KeySize != 256 || Fingerprint(publicKey) != request.Fingerprint)
            {
                return CommandAnswer.Refused("The key isn't a P-256 key with that fingerprint.");
            }
        }
        catch (Exception e) when (e is FormatException or CryptographicException)
        {
            return CommandAnswer.Refused("The public key isn't readable.");
        }
        if (await db.SupportAccessKeys.IgnoreQueryFilters()
                .AnyAsync(k => k.SchoolId == school.Id && k.Fingerprint == request.Fingerprint))
        {
            return CommandAnswer.Ok(new { request.TicketId, request.Fingerprint });
        }

        var now = clock.GetUtcNow();
        // A newer request replaces one the school hasn't decided.
        foreach (var pending in await db.SupportAccessKeys.IgnoreQueryFilters()
                     .Where(k => k.SchoolId == school.Id && k.TicketId == request.TicketId
                         && k.Status == SupportAccessKeyStatus.Pending)
                     .ToListAsync())
        {
            pending.Status = SupportAccessKeyStatus.Expired;
        }
        var key = new SupportAccessKey
        {
            SchoolId = school.Id,
            TicketId = request.TicketId,
            TicketTitle = Truncate(request.TicketTitle, 200) ?? $"#{request.TicketId}",
            TicketAuthorUserId = request.TicketAuthorUserId,
            PublicKey = publicKey,
            Fingerprint = request.Fingerprint,
            OperatorId = request.OperatorId,
            OperatorName = Truncate(request.OperatorName, 200) ?? "podpora",
            RequestedAsUserIds = [.. (request.RequestedAs ?? []).Select(u => u.UserId)],
            Reason = Truncate(request.Reason, MaxReasonLength),
            Status = SupportAccessKeyStatus.Pending,
            CreatedAt = now,
        };
        db.SupportAccessKeys.Add(key);
        switch (school.SupportAccess)
        {
            case SupportAccess.Never:
                Decide(key, SupportAccessKeyStatus.Rejected, null, "Škola vstup podpory nepovoluje.");
                break;
            case SupportAccess.Always:
                Approve(key, null, now + options.Value.ApprovalLength,
                    school.SupportAs == SupportAsScope.Users ? key.RequestedAsUserIds : [], null);
                break;
        }
        await db.SaveChangesAsync();
        return CommandAnswer.Ok(new { request.TicketId, request.Fingerprint });
    }

    /// <summary>A one-time challenge for entering with the ticket's approved key, and where the
    /// operator's browser takes it.</summary>
    public async Task<CommandAnswer> ChallengeAsync(JsonElement payload)
    {
        var request = payload.Deserialize<ChallengeRequest>(JsonSerializerOptions.Web)
            ?? throw new JsonException("No payload.");
        var now = clock.GetUtcNow();
        var key = await db.SupportAccessKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.SchoolId == request.SchoolId && k.TicketId == request.TicketId
                && k.Fingerprint == request.Fingerprint);
        if (key is null || !key.Usable(now))
        {
            return CommandAnswer.Refused("Škola tento klíč neschválila, nebo už schválení vypršelo.");
        }
        var school = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == key.SchoolId);
        if (school.SupportAccess == SupportAccess.Never)
        {
            return CommandAnswer.Refused("Škola vstup podpory nepovoluje.");
        }
        if (!Allows(key, request.AsUserId))
        {
            return CommandAnswer.Refused("Za tohoto uživatele škola vstup neschválila.");
        }
        if (links.Value.SchoolOrigin(school.Slug) is not { } origin)
        {
            return CommandAnswer.Refused("Instance nemá nastavenou adresu aplikace (App:FrontendBaseUrl / App:MainDomain).");
        }
        db.SupportEntryChallenges.RemoveRange(db.SupportEntryChallenges.Where(c => c.ExpiresAt < now - ClockSkew));
        var challenge = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expiresAt = now + ChallengeLifetime;
        db.SupportEntryChallenges.Add(new SupportEntryChallenge
        {
            KeyId = key.Id,
            Hash = Hash(challenge),
            AsUserId = request.AsUserId,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
        return CommandAnswer.Ok(new { challenge, expiresAt, enterUrl = $"{origin}/support/enter" });
    }

    /// <summary>The operator ends entering from the backoffice: the ticket's sessions end now.</summary>
    public async Task<CommandAnswer> EndAsync(JsonElement payload)
    {
        var schoolId = payload.TryGetProperty("schoolId", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : (int?)null;
        var ticketId = payload.TryGetProperty("ticketId", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : (int?)null;
        var now = clock.GetUtcNow();
        var query = db.SupportSessions.IgnoreQueryFilters().Where(x => x.EndedAt == null && x.ExpiresAt > now);
        if (schoolId is not null)
        {
            query = query.Where(x => x.SchoolId == schoolId);
        }
        if (ticketId is not null)
        {
            query = query.Where(x => x.TicketId == ticketId);
        }
        var ended = await query.ToListAsync();
        foreach (var session in ended)
        {
            End(session, now, session.SuperAdminUsername, "Podpora vstup ukončila z backoffice.");
        }
        await db.SaveChangesAsync();
        return CommandAnswer.Ok(new { ended = ended.Count });
    }

    // ---- The school's admin ----

    /// <param name="asUserIds">Whom the support may be (<see cref="SupportAsScope.Users"/>);
    /// ignored when the school lets it in as its admin.</param>
    public async Task<string?> ApproveAsync(
        SupportAccessKey key, School school, User by, string byName, DateTimeOffset until, IReadOnlyList<int>? asUserIds)
    {
        var now = clock.GetUtcNow();
        if (key.Status is not (SupportAccessKeyStatus.Pending or SupportAccessKeyStatus.Approved))
        {
            return "O tomto klíči už je rozhodnuto.";
        }
        if (school.SupportAccess == SupportAccess.Never)
        {
            return "Škola vstup podpory nepovoluje — změňte to v Nastavení.";
        }
        if (until <= now || until > now + TimeSpan.FromDays(30))
        {
            return "Platnost zvolte nejvýš na 30 dní dopředu.";
        }
        List<int> users = [];
        if (school.SupportAs == SupportAsScope.Users)
        {
            var ids = (asUserIds ?? []).Distinct().ToList();
            if (ids.Count == 0)
            {
                return "Vyberte, za koho smí podpora vstoupit.";
            }
            var known = (await SupportAsPolicy.AccountsAsync(db, school.Id, ids)).Select(u => u.Id).ToList();
            if (known.Count != ids.Count)
            {
                return "Někdo z vybraných uživatelů ve škole není.";
            }
            users = ids;
        }
        Approve(key, by.Id, until, users, byName);
        // Sessions as someone no longer approved end.
        foreach (var session in await RunningAsync(key, now))
        {
            session.SupportAsUserIds = users;
            if (session.AsUserId is { } asUserId ? !users.Contains(asUserId) : school.SupportAs == SupportAsScope.Users)
            {
                End(session, now, byName, SupportAsPolicy.ChangedReason);
            }
        }
        return null;
    }

    public string? Reject(SupportAccessKey key, User by, string byName, string? note)
    {
        if (key.Status != SupportAccessKeyStatus.Pending)
        {
            return "O tomto klíči už je rozhodnuto.";
        }
        key.DecidedByUserId = by.Id;
        Decide(key, SupportAccessKeyStatus.Rejected, byName, Truncate(note, MaxReasonLength));
        return null;
    }

    /// <summary>The key stops working; the sessions entered with it end now.</summary>
    public async Task<string?> RevokeAsync(SupportAccessKey key, User by, string byName)
    {
        if (key.Status != SupportAccessKeyStatus.Approved)
        {
            return "Odvolat jde jen schválený klíč.";
        }
        key.DecidedByUserId = by.Id;
        Decide(key, SupportAccessKeyStatus.Revoked, byName, null);
        var now = clock.GetUtcNow();
        foreach (var session in await RunningAsync(key, now))
        {
            End(session, now, byName, RevokedReason);
        }
        return null;
    }

    /// <summary>The school never lets the support in any more: every approved key is revoked.</summary>
    public async Task RevokeAllAsync(int schoolId, User by, string byName)
    {
        foreach (var key in await db.SupportAccessKeys
                     .Where(k => k.SchoolId == schoolId && (k.Status == SupportAccessKeyStatus.Approved
                         || k.Status == SupportAccessKeyStatus.Pending))
                     .ToListAsync())
        {
            if (key.Status == SupportAccessKeyStatus.Pending)
            {
                Reject(key, by, byName, "Škola vstup podpory nepovoluje.");
            }
            else
            {
                await RevokeAsync(key, by, byName);
            }
        }
    }

    /// <summary>The ticket ended: its keys are no use any more, its sessions end.</summary>
    public async Task ExpireAsync(int schoolId, int ticketId)
    {
        var now = clock.GetUtcNow();
        foreach (var key in await db.SupportAccessKeys.IgnoreQueryFilters()
                     .Where(k => k.SchoolId == schoolId && k.TicketId == ticketId
                         && (k.Status == SupportAccessKeyStatus.Approved || k.Status == SupportAccessKeyStatus.Pending))
                     .ToListAsync())
        {
            key.Status = SupportAccessKeyStatus.Expired;
            foreach (var session in await RunningAsync(key, now))
            {
                End(session, now, "podpora", "Požadavek skončil.");
            }
        }
    }

    // ---- Entering ----

    /// <summary>The signed entry from the operator's browser (<c>#entry=</c> of the entry page):
    /// the challenge, the key the school approved and its signature, as whom → the support's
    /// login. Null with nothing told: a link that doesn't work says only <see cref="Invalid"/>.</summary>
    public async Task<LoginResponse?> EnterAsync(HttpContext http, string? entry)
    {
        var parts = entry?.Split('.');
        if (parts is not [{ Length: > 0 and < 4000 } payloadText, { Length: > 0 and < 200 } signatureText]
            || backoffice.Connection is not { } connection)
        {
            return null;
        }
        EntryPayload? payload;
        byte[] signature;
        try
        {
            payload = JsonSerializer.Deserialize<EntryPayload>(Base64Url.DecodeFromChars(payloadText), JsonSerializerOptions.Web);
            signature = Base64Url.DecodeFromChars(signatureText);
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;
        }
        var now = clock.GetUtcNow();
        if (payload is not { V: 1, Challenge.Length: > 0 } || payload.InstanceId != connection.InstanceId
            || payload.IssuedAt > now + ClockSkew)
        {
            return null;
        }
        var hash = Hash(payload.Challenge);
        // Anonymous: no school yet.
        var challenge = await db.SupportEntryChallenges.IgnoreQueryFilters().Include(c => c.Key)
            .FirstOrDefaultAsync(c => c.Hash == hash);
        if (challenge is null)
        {
            return null;
        }
        // One use, whatever comes of it.
        db.SupportEntryChallenges.Remove(challenge);
        await db.SaveChangesAsync();
        var key = challenge.Key;
        if (challenge.ExpiresAt + ClockSkew < now || !key.Usable(now)
            || key.Fingerprint != payload.Fingerprint || key.TicketId != payload.TicketId
            || key.SchoolId != payload.SchoolId || challenge.AsUserId != payload.AsUserId
            || !Verify(key.PublicKey, Encoding.ASCII.GetBytes(payloadText), signature)
            || !Allows(key, payload.AsUserId))
        {
            return null;
        }
        var school = await db.Schools.IgnoreQueryFilters().FirstAsync(s => s.Id == key.SchoolId);
        if (school.SupportAccess == SupportAccess.Never)
        {
            return null;
        }
        var carrier = await db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.IsSuperAdmin && u.SchoolId == null && u.Username == SupportController.BuiltInUsername);
        if (carrier is null)
        {
            return null;
        }
        var session = new SupportSession
        {
            SuperAdminUserId = carrier.Id,
            SuperAdminUsername = key.OperatorName,
            OperatorId = key.OperatorId,
            AccessKeyId = key.Id,
            SchoolId = school.Id,
            TicketId = key.TicketId,
            TicketTitle = key.TicketTitle,
            TicketAuthorUserId = key.TicketAuthorUserId,
            SupportAsUserIds = key.ApprovedAsUserIds,
        };
        if (payload.AsUserId is { } asUserId)
        {
            session.School = school;
            if (await sessions.SetAsAsync(session, asUserId, null) is not null)
            {
                return null;
            }
        }
        else if (!SupportAsPolicy.AllowsAdmin(school))
        {
            return null;
        }
        await sessions.StartAsync(session);
        Outbox.Add(db, backoffice, SessionStartedEvent,
            new { ticketId = key.TicketId, fingerprint = key.Fingerprint, sessionId = session.Id, at = now }, clock);
        await db.SaveChangesAsync();
        return await sessions.BeginAsync(http, session);
    }

    private static bool Verify(byte[] publicKey, byte[] data, byte[] signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>As the user (one the school approved), or as the admin when the school lets the
    /// support in as its admin.</summary>
    private static bool Allows(SupportAccessKey key, int? asUserId) =>
        asUserId is { } id ? key.ApprovedAsUserIds.Contains(id) : key.ApprovedAsUserIds.Count == 0;

    private Task<List<SupportSession>> RunningAsync(SupportAccessKey key, DateTimeOffset now) =>
        db.SupportSessions.IgnoreQueryFilters()
            .Where(s => s.AccessKeyId == key.Id && s.EndedAt == null && s.ExpiresAt > now)
            .ToListAsync();

    private static void End(SupportSession session, DateTimeOffset now, string by, string reason)
    {
        session.EndedAt = now;
        session.RefreshSecretHash = null;
        session.EndedBy = by;
        session.EndReason = reason;
    }

    private void Approve(SupportAccessKey key, int? byUserId, DateTimeOffset until, List<int> users, string? byName)
    {
        key.ApprovedUntil = until;
        key.ApprovedAsUserIds = users;
        key.DecidedByUserId = byUserId;
        Decide(key, SupportAccessKeyStatus.Approved, byName, null);
    }

    /// <summary>Tells the backoffice; <paramref name="byName"/> null: the school's setting decided.</summary>
    private void Decide(SupportAccessKey key, SupportAccessKeyStatus status, string? byName, string? note)
    {
        var now = clock.GetUtcNow();
        key.Status = status;
        key.DecidedAt = now;
        key.DecidedByName = byName ?? "nastavení školy";
        key.DecisionNote = note;
        var names = key.ApprovedAsUserIds.Count == 0
            ? []
            : db.Users.IgnoreQueryFilters().Include(u => u.Person)
                .Where(u => key.ApprovedAsUserIds.Contains(u.Id)).AsEnumerable()
                .Select(u => new { userId = u.Id, name = People.PersonNames.Display(u) })
                .ToList();
        Outbox.Add(db, backoffice, DecidedEvent, new
        {
            ticketId = key.TicketId,
            fingerprint = key.Fingerprint,
            status = status.ToString(),
            until = status == SupportAccessKeyStatus.Approved ? key.ApprovedUntil : null,
            @as = names,
            by = key.DecidedByUserId is { } id ? new { userId = id, name = key.DecidedByName } : null,
            note,
            at = now,
        }, clock);
    }

    public static string Fingerprint(byte[] publicKey) =>
        "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicKey)).TrimEnd('=');

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string? Truncate(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private sealed record UserRef(int UserId, string Name);

    private sealed record AccessRequest(
        int TicketId, int SchoolId, string? TicketTitle, int? TicketAuthorUserId, string PublicKey, string Fingerprint,
        int OperatorId, string? OperatorName, List<UserRef>? RequestedAs, string? Reason);

    private sealed record ChallengeRequest(int TicketId, int SchoolId, string Fingerprint, int? AsUserId);

    /// <summary>As the backoffice signs it (soovioo-backoffice#19).</summary>
    private sealed record EntryPayload(
        int V, string Challenge, Guid InstanceId, int SchoolId, int TicketId, string Fingerprint, int? AsUserId,
        int OperatorId, string OperatorName, DateTimeOffset IssuedAt);
}
