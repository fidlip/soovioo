namespace Sova.Api.Homework;

/// <summary>Same shape and storage mechanism as Messaging.MessageAttachment
/// (local disk via the shared MessageAttachmentStorageService, same
/// MessageAttachmentRules allow-list/size cap) — kept as its own entity
/// rather than a shared base type since it points at a different parent
/// (Homework, not Message) and the two modules don't otherwise share
/// code.</summary>
public class HomeworkAttachment
{
    public int Id { get; set; }

    public int HomeworkId { get; set; }
    public Homework? Homework { get; set; }

    public int SchoolId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string StoragePath { get; set; }
}
