using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Soovioo.Api.Live;

namespace Soovioo.Api.Tests.Live;

internal record LiveEvent(string Method, IReadOnlyList<string> To);

/// <summary>What would be sent to the open tabs.</summary>
internal class RecordingHub : IHubContext<LiveHub>
{
    public readonly List<LiveEvent> Sent = [];

    public IHubClients Clients => new RecordingClients(this);
    public IGroupManager Groups => throw new NotSupportedException();

    public LivePublisher Publisher() => new(this, NullLogger<LivePublisher>.Instance);

    private class RecordingClients(RecordingHub hub) : IHubClients
    {
        public IClientProxy Users(IReadOnlyList<string> userIds) => new Proxy(hub, userIds);
        public IClientProxy Group(string groupName) => new Proxy(hub, [groupName]);
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => new Proxy(hub, groupNames);

        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
    }

    private class Proxy(RecordingHub hub, IReadOnlyList<string> to) : IClientProxy
    {
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            hub.Sent.Add(new LiveEvent(method, to));
            return Task.CompletedTask;
        }
    }
}
