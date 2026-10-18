using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Soovioo.Api.Tests;

/// <summary>Points ContentRootPath at a fresh temp directory per instance —
/// used by MessageAttachmentStorageService tests so uploaded files land
/// somewhere disposable, never the real backend's AttachmentStorage
/// folder.</summary>
internal class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment()
    {
        ContentRootPath = Path.Combine(Path.GetTempPath(), "soovioo-test-" + Guid.NewGuid());
        Directory.CreateDirectory(ContentRootPath);
    }

    public string ApplicationName { get; set; } = "Soovioo.Api.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Test";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
}
