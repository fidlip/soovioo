namespace Sova.Api.Messaging;

/// <summary>Shared between MessagesController (single recipient) and
/// MessageBroadcastController (class/school fan-out) — both accept the
/// same attachment rules. Validated by extension only, not by the
/// client-supplied Content-Type, which is easy to spoof and not worth
/// trusting for a simple allow-list like this.</summary>
public static class MessageAttachmentRules
{
    public const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".txt",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", // MS Office
        ".odt", ".ods", ".odp", // LibreOffice/OpenOffice
    };

    public static bool IsAllowedFileName(string fileName) =>
        AllowedExtensions.Contains(Path.GetExtension(fileName));
}
