using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sova.Api.Data;
using Sova.Api.Evidence;
using Sova.Api.Messaging;

namespace Sova.Api.Tests.Messaging;

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

        var result = await new MessagesController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteMessage(student.Id, message.Id);

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

        var result = await new MessagesController(db, new FakeCurrentSchoolContext(SchoolId))
            .DeleteMessage(student.Id + 1, message.Id);

        Assert.IsType<NotFoundResult>(result);
    }
}
