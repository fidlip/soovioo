using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Sova.Api.Tests;

/// <summary>Points ContentRootPath at a fresh temp directory per instance —
/// used by MessageAttachmentStorageService tests so uploaded files land
/// somewhere disposable, never the real backend's AttachmentStorage
/// folder.</summary>
internal class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment()
    {
        ContentRootPath = Path.Combine(Path.GetTempPath(), "sova-test-" + Guid.NewGuid());
        Directory.CreateDirectory(ContentRootPath);
    }

    public string ApplicationName { get; set; } = "Sova.Api.Tests";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string ContentRootPath { get; set; }
    public string EnvironmentName { get; set; } = "Test";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = "";
}
