using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;

namespace Soovioo.Api.Messaging;

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

    /// <summary>Demo schools take anyone's uploads with no account behind
    /// them — smaller files and a cap on the whole demo.</summary>
    public const long DemoMaxSizeBytes = 2 * 1024 * 1024;
    public const long DemoMaxTotalBytes = 50 * 1024 * 1024;

    /// <summary>Null when the upload fits a demo school's limits (or the
    /// school isn't a demo), the error message otherwise.</summary>
    public static async Task<string?> DemoLimitErrorAsync(AppDbContext db, int schoolId, long sizeBytes)
    {
        if (!await db.Schools.IgnoreQueryFilters().AnyAsync(s => s.Id == schoolId && s.IsDemo))
        {
            return null;
        }
        if (sizeBytes > DemoMaxSizeBytes)
        {
            return "V ukázkové škole je limit 2 MB na soubor.";
        }
        var used = await db.MessageAttachments.IgnoreQueryFilters().Where(a => a.SchoolId == schoolId).SumAsync(a => a.SizeBytes)
            + await db.HomeworkAttachments.IgnoreQueryFilters().Where(a => a.SchoolId == schoolId).SumAsync(a => a.SizeBytes)
            + await db.ExcuseAttachments.IgnoreQueryFilters().Where(a => a.SchoolId == schoolId).SumAsync(a => a.SizeBytes);
        return used + sizeBytes > DemoMaxTotalBytes
            ? "Ukázková škola už má nahraný maximální objem souborů (50 MB)."
            : null;
    }
}
