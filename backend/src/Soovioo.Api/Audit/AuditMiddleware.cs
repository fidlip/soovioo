using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.People;
using Soovioo.Api.Support;

namespace Soovioo.Api.Audit;

/// <summary>Writes an <see cref="AuditLogEntry"/> for every data-changing
/// API request (anything but GET/HEAD/OPTIONS) — whoever makes it, support
/// mode included. Token refreshes aren't changes and are skipped. A failure
/// to write the log never fails the request itself.</summary>
public class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    /// <summary>How much of the request/response body is looked at.</summary>
    private const int MaxCapturedBytes = 64 * 1024;

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (!ShouldAudit(context.Request))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        var originalBody = context.Response.Body;
        var capture = new CaptureStream(originalBody, MaxCapturedBytes);
        context.Response.Body = capture;
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
            try
            {
                await WriteAsync(context, db, failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode,
                    capture.Captured);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Audit log write failed for {Method} {Path}", context.Request.Method, context.Request.Path);
            }
        }
    }

    public static bool ShouldAudit(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method)
        && !request.Path.StartsWithSegments("/api/auth/refresh");

    private static async Task WriteAsync(HttpContext context, AppDbContext db, int statusCode, byte[] responseBytes)
    {
        var request = context.Request;
        var user = context.User;
        var audit = AuditContext.Find(context);
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();

        // Whatever the endpoint left tracked must not be saved along with
        // the log entry (e.g. a change it rejected without saving).
        db.ChangeTracker.Clear();

        var (payloadText, payload) = await ReadPayloadAsync(request);

        var userId = ParseInt(user.FindFirstValue(ClaimTypes.NameIdentifier)) ?? audit?.UserId;
        var schoolId = ParseInt(user.FindFirstValue("schoolId")) ?? audit?.SchoolId;
        var username = user.FindFirstValue(ClaimTypes.Name) ?? audit?.Username;
        string? displayName = null;
        if (userId is not null)
        {
            var account = await db.Users.IgnoreQueryFilters().Include(u => u.Person)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (account is not null)
            {
                username ??= account.Username;
                displayName = PersonNames.Display(account);
                // Logout carries no token — the account's own school.
                if (user.Identity?.IsAuthenticated != true)
                {
                    schoolId ??= account.SchoolId;
                }
            }
        }

        // The class the change concerns: named directly, or via a student.
        var schoolClassId = ParseInt(request.RouteValues["schoolClassId"]?.ToString())
                            ?? AuditPayload.Int(payload, "schoolClassId");
        string? studentName = null;
        if (AuditPayload.Int(payload, "studentId") is { } studentId)
        {
            var student = await db.Students.IgnoreQueryFilters()
                .Where(s => s.Id == studentId && (schoolId == null || s.SchoolId == schoolId))
                .Select(s => new { s.SchoolClassId, s.FirstName, s.LastName })
                .FirstOrDefaultAsync();
            if (student is not null)
            {
                schoolClassId ??= student.SchoolClassId;
                studentName = $"{student.FirstName} {student.LastName}";
            }
        }

        var responseText = Encoding.UTF8.GetString(responseBytes);
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            SchoolId = schoolId,
            UserId = userId,
            Username = username,
            UserDisplayName = displayName,
            Role = user.FindFirstValue(ClaimTypes.Role),
            SupportSessionId = ParseInt(user.FindFirstValue(SupportClaims.SessionId)) ?? audit?.SupportSessionId,
            SupportBy = user.FindFirstValue(SupportClaims.By) ?? audit?.SupportBy,
            Method = request.Method,
            Path = request.Path.Value ?? "",
            Area = AuditDescriptions.Area(action?.ControllerName),
            Description = audit?.Description
                          ?? Describe(AuditDescriptions.Describe(action?.ControllerName, action?.ActionName),
                              AuditPayload.SubjectName(payload), studentName, request.RouteValues["id"]?.ToString(),
                              statusCode >= 400),
            StatusCode = statusCode,
            RequestBody = payloadText,
            Result = AuditPayload.SummarizeResponse(statusCode, responseText),
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            SchoolClassId = schoolClassId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>"Vytvořen uživatel Karel Nový", "Zapsána známka – Jan Novák",
    /// "Smazána učebna #4"; a rejected request "… (neprovedeno)".</summary>
    private static string? Describe(string? description, string? subject, string? student, string? id, bool failed)
    {
        if (description is null)
        {
            return null;
        }
        var text = description;
        if (subject is not null)
        {
            text += $" {subject}";
        }
        if (student is not null && student != subject)
        {
            text += $" – {student}";
        }
        if (subject is null && student is null && id is not null)
        {
            text += $" #{id}";
        }
        return failed ? $"{text} (neprovedeno)" : text;
    }

    private static async Task<(string? Text, JsonObject? Json)> ReadPayloadAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            return (AuditPayload.DescribeForm(await request.ReadFormAsync()), null);
        }
        if (!request.Body.CanSeek)
        {
            return (null, null);
        }
        request.Body.Position = 0;
        var buffer = new byte[MaxCapturedBytes];
        var read = 0;
        int n;
        while (read < buffer.Length && (n = await request.Body.ReadAsync(buffer.AsMemory(read))) > 0)
        {
            read += n;
        }
        var raw = Encoding.UTF8.GetString(buffer, 0, read);
        JsonObject? json = null;
        try
        {
            json = string.IsNullOrWhiteSpace(raw) ? null : JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
        }
        return (AuditPayload.MaskJson(raw), json);
    }

    private static int? ParseInt(string? value) => int.TryParse(value, out var number) ? number : null;

    /// <summary>Passes the response through untouched, keeping a copy of its
    /// first bytes.</summary>
    private sealed class CaptureStream(Stream inner, int limit) : Stream
    {
        private readonly MemoryStream copy = new();

        public byte[] Captured => copy.ToArray();

        private void Keep(ReadOnlySpan<byte> data)
        {
            var room = limit - (int)copy.Length;
            if (room > 0)
            {
                copy.Write(data[..Math.Min(room, data.Length)]);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Keep(buffer.AsSpan(offset, count));
            inner.Write(buffer, offset, count);
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Keep(buffer.Span);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
