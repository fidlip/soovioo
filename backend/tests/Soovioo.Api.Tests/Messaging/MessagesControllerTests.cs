using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Soovioo.Api.Data;
using Soovioo.Api.Evidence;
using Soovioo.Api.Messaging;

namespace Soovioo.Api.Tests.Messaging;

public class MessagesControllerTests
{
    private const int SchoolId = 1;

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options, new FakeCurrentSchoolContext(SchoolId));
    }

    /// <summary>Teacher role — GetAccessibleStudentIdsAsync short-circuits to
    /// "no restriction" for Teacher/Admin, so CanAccessStudentAsync (used by
    /// GetMessages/DownloadAttachment) doesn't NRE on a missing ClaimsPrincipal.</summary>
    private static MessagesController NewController(AppDbContext db)
    {
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Role, "Teacher"),
            new Claim(ClaimTypes.Name, "ucitel"),
        ]);
        return new MessagesController(db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(new FakeWebHostEnvironment()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
            },
        };
    }

    private static IFormFile FakeFile(string fileName, string content = "hello", string contentType = "text/plain")
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public async Task DeleteMessageRemovesIt()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id,
            SenderName = "ucitel",
            Subject = "Ahoj",
            Body = "Text",
            SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var result = await NewController(db).DeleteMessage(student.Id, message.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await db.Messages.FindAsync(message.Id));
    }

    [Fact]
    public async Task DeleteMessageReturnsNotFoundForWrongStudent()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id,
            SenderName = "ucitel",
            Subject = "Ahoj",
            Body = "Text",
            SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var result = await NewController(db).DeleteMessage(student.Id + 1, message.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task UploadAttachmentSucceedsAndIsReturnedByGetMessages()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        var controller = NewController(db);

        var uploadResult = await controller.UploadAttachment(student.Id, message.Id, FakeFile("rozvrh.pdf", contentType: "application/pdf"));
        var attachmentDto = Assert.IsType<MessageAttachmentDto>(Assert.IsType<OkObjectResult>(uploadResult.Result).Value);
        Assert.Equal("rozvrh.pdf", attachmentDto.FileName);

        var getResult = await controller.GetMessages(student.Id);
        var messages = Assert.IsAssignableFrom<IEnumerable<MessageDto>>(
            Assert.IsType<OkObjectResult>(getResult.Result).Value).ToList();
        var attachment = Assert.Single(Assert.Single(messages).Attachments);
        Assert.Equal("rozvrh.pdf", attachment.FileName);
    }

    [Fact]
    public async Task UploadAttachmentRejectsDisallowedExtension()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var result = await NewController(db).UploadAttachment(student.Id, message.Id, FakeFile("virus.exe"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadAttachmentRejectsOversizedFile()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        var tooBig = new FormFile(new MemoryStream(new byte[1]), 0, MessageAttachmentRules.MaxSizeBytes + 1, "file", "big.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf",
        };

        var result = await NewController(db).UploadAttachment(student.Id, message.Id, tooBig);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UploadAttachmentReturnsNotFoundForWrongStudent()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();

        var result = await NewController(db).UploadAttachment(student.Id + 1, message.Id, FakeFile("rozvrh.pdf"));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task DownloadAttachmentServesTheFile()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        var controller = NewController(db);
        var uploadResult = await controller.UploadAttachment(student.Id, message.Id, FakeFile("rozvrh.pdf", contentType: "application/pdf"));
        var attachmentDto = (MessageAttachmentDto)Assert.IsType<OkObjectResult>(uploadResult.Result).Value!;

        var downloadResult = await controller.DownloadAttachment(student.Id, message.Id, attachmentDto.Id);

        var file = Assert.IsType<PhysicalFileResult>(downloadResult);
        Assert.Equal("rozvrh.pdf", file.FileDownloadName);
        Assert.Equal("application/pdf", file.ContentType);
    }

    /// <summary>Deleting the message must also remove the attachment's
    /// physical file from disk, not just the DB row (see
    /// MessagesController.DeleteMessage).</summary>
    [Fact]
    public async Task DeleteMessageRemovesAttachmentFileFromDisk()
    {
        using var db = NewDb();
        var student = new Student { FirstName = "Jan", LastName = "Novák", SchoolClassId = 1, SchoolId = SchoolId };
        db.Students.Add(student);
        await db.SaveChangesAsync();
        var message = new Message
        {
            StudentId = student.Id, SenderName = "ucitel", Subject = "Ahoj", Body = "Text", SchoolId = SchoolId,
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        var env = new FakeWebHostEnvironment();
        var controller = new MessagesController(db, new FakeCurrentSchoolContext(SchoolId), new MessageAttachmentStorageService(env));
        var uploadResult = await controller.UploadAttachment(student.Id, message.Id, FakeFile("rozvrh.pdf", contentType: "application/pdf"));
        var attachmentDto = (MessageAttachmentDto)Assert.IsType<OkObjectResult>(uploadResult.Result).Value!;
        var storedFile = Directory.GetFiles(env.ContentRootPath, "*", SearchOption.AllDirectories).Single();
        Assert.True(File.Exists(storedFile));

        await controller.DeleteMessage(student.Id, message.Id);

        Assert.False(File.Exists(storedFile));
    }
}
