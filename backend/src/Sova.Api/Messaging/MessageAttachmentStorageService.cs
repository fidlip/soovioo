namespace Sova.Api.Messaging;

/// <summary>Attachments live on local disk outside wwwroot — never directly
/// web-servable, downloaded only through MessagesController's authorized
/// endpoint (same access check as GetMessages). Layout: {ContentRoot}/
/// AttachmentStorage/{schoolId}/{guid}{extension}. The GUID filename avoids
/// path traversal and collisions; the original name is kept separately on
/// MessageAttachment.FileName for the download's Content-Disposition
/// header.</summary>
public class MessageAttachmentStorageService(IWebHostEnvironment env)
{
    private string RootPath => Path.Combine(env.ContentRootPath, "AttachmentStorage");

    public async Task<string> SaveAsync(int schoolId, IFormFile file)
    {
        var schoolDir = Path.Combine(RootPath, schoolId.ToString());
        Directory.CreateDirectory(schoolDir);
        var storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var fullPath = Path.Combine(schoolDir, storedName);
        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream);
        }
        return Path.Combine(schoolId.ToString(), storedName);
    }

    public string GetFullPath(string relativePath) => Path.Combine(RootPath, relativePath);

    public void Delete(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
