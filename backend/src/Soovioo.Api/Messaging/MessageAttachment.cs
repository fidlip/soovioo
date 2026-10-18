namespace Soovioo.Api.Messaging;

public class MessageAttachment
{
    public int Id { get; set; }

    public int MessageId { get; set; }
    public Message? Message { get; set; }

    public int SchoolId { get; set; }

    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Relative to MessageAttachmentStorageService's storage root —
    /// a generated GUID-based name, never the original FileName, to avoid
    /// path traversal and collisions. Exclusively owned by this one row:
    /// every upload gets its own physical copy (even a second upload of the
    /// same broadcast file to a different recipient's message gets saved
    /// again), so deleting a message never needs to check whether another
    /// row still references the same file.</summary>
    public required string StoragePath { get; set; }
}
