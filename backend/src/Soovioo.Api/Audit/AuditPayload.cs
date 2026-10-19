using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soovioo.Api.Audit;

/// <summary>Turns request/response bodies into what the audit log keeps:
/// the payload with secrets masked, and from the response only an error
/// message or the new record's id.</summary>
public static class AuditPayload
{
    public const int MaxStoredLength = 4000;
    private const string Masked = "***";

    /// <summary>Hesla, tokeny, tajné kódy — matched case-insensitively on the
    /// property name, at any depth.</summary>
    public static bool IsSensitive(string propertyName)
    {
        var name = propertyName.ToLowerInvariant();
        return name.Contains("password") || name.Contains("token") || name.Contains("secret")
               || name == "code";
    }

    /// <summary>The JSON with sensitive values masked; null for an empty
    /// body; a non-JSON body is kept only as its length.</summary>
    public static string? MaskJson(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return $"(data mimo JSON, {Encoding.UTF8.GetByteCount(body)} B)";
        }
        if (node is JsonObject { Count: 0 } or JsonArray { Count: 0 } or null)
        {
            return null;
        }
        Mask(node);
        return Truncate(node?.ToJsonString(new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
    }

    private static void Mask(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (IsSensitive(key))
                    {
                        if (obj[key] is not null)
                        {
                            obj[key] = Masked;
                        }
                    }
                    else
                    {
                        Mask(obj[key]);
                    }
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Mask(item);
                }
                break;
        }
    }

    /// <summary>Form posts (file uploads): field names/values (masked) and
    /// files as name + size, never their content.</summary>
    public static string? DescribeForm(IFormCollection form)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in form)
        {
            obj[key] = IsSensitive(key) ? Masked : value.ToString();
        }
        foreach (var file in form.Files)
        {
            obj[file.Name] = $"{file.FileName} ({file.Length} B)";
        }
        return obj.Count == 0 ? null : Truncate(obj.ToJsonString());
    }

    /// <summary>Error text for a failure (plain string, ProblemDetails, or
    /// validation errors), "ID n" for a success that returns an object with
    /// an id; null otherwise.</summary>
    public static string? SummarizeResponse(int statusCode, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return statusCode >= 400 ? Truncate(body.Trim(), 500) : null;
        }

        if (statusCode >= 400)
        {
            return node switch
            {
                JsonValue value when value.TryGetValue<string>(out var text) => Truncate(text, 500),
                JsonObject obj => Truncate(ProblemText(obj), 500),
                _ => null,
            };
        }
        return node is JsonObject result && result["id"] is JsonValue id ? $"ID {id.ToJsonString().Trim('"')}" : null;
    }

    private static string? ProblemText(JsonObject problem)
    {
        if (problem["errors"] is JsonObject errors && errors.Count > 0)
        {
            return string.Join("; ", errors
                .SelectMany(e => e.Value is JsonArray messages
                    ? messages.Select(m => m?.GetValue<string>())
                    : [e.Value?.ToString()])
                .Where(m => !string.IsNullOrEmpty(m)));
        }
        return (problem["detail"] ?? problem["title"])?.GetValue<string>();
    }

    private static string? Truncate(string? text, int max = MaxStoredLength) =>
        text is null || text.Length <= max ? text : text[..max] + "…";

    /// <summary>A name for what the payload is about, for the description:
    /// a person's name, or a title/name/subject field.</summary>
    public static string? SubjectName(JsonObject? payload)
    {
        if (payload is null)
        {
            return null;
        }
        var fullName = $"{Text(payload, "firstName")} {Text(payload, "lastName")}".Trim();
        if (fullName.Length > 0)
        {
            return fullName;
        }
        return new[] { "name", "title", "subject", "username" }
            .Select(key => Text(payload, key))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    public static string? Text(JsonObject payload, string key) =>
        payload[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text.Trim() : null;

    public static int? Int(JsonObject? payload, string key) =>
        payload?[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
