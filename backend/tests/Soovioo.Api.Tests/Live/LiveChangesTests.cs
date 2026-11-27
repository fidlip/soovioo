using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Auth;
using Soovioo.Api.Data;
using Soovioo.Api.Live;
using Soovioo.Api.Messaging;
using Soovioo.Api.Notifications;
using Soovioo.Api.Support;

namespace Soovioo.Api.Tests.Live;

public class LiveChangesTests
{
    private readonly RecordingHub hub = new();
    private readonly string dbName = Guid.NewGuid().ToString();

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName)
            .AddInterceptors(new LiveChanges(hub.Publisher(), NullLogger<LiveChanges>.Instance))
            .Options,
        new FakeCurrentSchoolContext(null, isSuperAdmin: true));

    private static Notification Notification(int userId) =>
        new() { SchoolId = 1, UserId = userId, Type = NotificationType.Message, Title = "x", Page = "messages" };

    [Fact]
    public async Task ANotificationTellsItsUserAndTheAccountsLinkedToThem()
    {
        using var db = Db();
        var group = Guid.NewGuid();
        db.AccountLinks.AddRange(new AccountLink { UserId = 5, GroupId = group }, new AccountLink { UserId = 6, GroupId = group });
        await db.SaveChangesAsync();
        hub.Sent.Clear();

        db.Notifications.Add(Notification(5));
        await db.SaveChangesAsync();

        var sent = Assert.Single(hub.Sent);
        Assert.Equal(LiveEvents.Counts, sent.Method);
        Assert.Equal(["5", "6"], sent.To.Order());
    }

    [Fact]
    public async Task UnlinkingTellsTheAccountAndThoseStillLinked()
    {
        using var db = Db();
        var group = Guid.NewGuid();
        var leaving = new AccountLink { UserId = 5, GroupId = group };
        db.AccountLinks.AddRange(
            leaving, new AccountLink { UserId = 6, GroupId = group }, new AccountLink { UserId = 7, GroupId = group });
        await db.SaveChangesAsync();
        Assert.Equal(["5", "6", "7"], Assert.Single(hub.Sent).To.Order());
        hub.Sent.Clear();

        db.AccountLinks.Remove(leaving);
        await db.SaveChangesAsync();

        Assert.Equal(["5", "6", "7"], Assert.Single(hub.Sent).To.Order());
    }

    [Fact]
    public async Task OnlyPushingANotificationTellsNoOne()
    {
        using var db = Db();
        var notification = Notification(5);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        hub.Sent.Clear();

        notification.PushedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Empty(hub.Sent);

        notification.ReadAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(["5"], Assert.Single(hub.Sent).To);
    }

    [Fact]
    public async Task AMessageTellsEveryoneInTheConversation()
    {
        using var db = Db();
        var conversation = new Conversation { SchoolId = 1, Subject = "Výlet", CreatedByUserId = 5 };
        conversation.Participants.Add(new ConversationParticipant { UserId = 5, SchoolId = 1 });
        conversation.Participants.Add(new ConversationParticipant { UserId = 7, SchoolId = 1 });
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        hub.Sent.Clear();

        db.Messages.Add(new Message { SchoolId = 1, ConversationId = conversation.Id, SenderUserId = 5, SenderName = "A", Body = "Ahoj" });
        await db.SaveChangesAsync();

        Assert.Equal(["5", "7"], Assert.Single(hub.Sent).To.Order());
    }

    [Fact]
    public async Task ATicketsThreadTellsItsDetail()
    {
        using var db = Db();
        db.SupportTicketEntries.Add(new SupportTicketEntry
        {
            TicketId = 3, SchoolId = 1, Kind = SupportTicketEntryKind.Reset, AuthorName = "Ředitel",
        });
        await db.SaveChangesAsync();

        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Ticket && s.To.SequenceEqual(["ticket:1:3"]));
    }

    [Fact]
    public async Task ASessionEndingTellsItsTabTheTicketAndWhomItWasInAs()
    {
        using var db = Db();
        var session = new SupportSession
        {
            SuperAdminUsername = "superadmin", SchoolId = 1, TicketId = 3, TicketAuthorUserId = 8, AsUserId = 9,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
        };
        db.SupportSessions.Add(session);
        await db.SaveChangesAsync();
        hub.Sent.Clear();

        session.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Empty(hub.Sent);

        session.EndedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Counts && s.To.Order().SequenceEqual(["8", "9"]));
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Ticket && s.To.SequenceEqual(["ticket:1:3"]));
        Assert.Contains(hub.Sent, s => s.Method == LiveEvents.Support && s.To.SequenceEqual([LiveGroups.Session(session.Id)]));

        hub.Sent.Clear();
        session.DisconnectedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(LiveEvents.Ticket, Assert.Single(hub.Sent).Method);
    }
}
