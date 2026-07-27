using DotBoxD.Services.Client;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport.ValueTaskTimeout;

public sealed class PendingDirectCompletionHandshakeTests
{
    [Fact]
    public void Completion_before_owner_notifies_on_owner_publication()
    {
        var handshake = new PendingDirectCompletionHandshake();

        Assert.Equal(
            PendingDirectCompletionAction.None,
            handshake.PublishCompletion(sendCancel: false));
        Assert.Equal(
            PendingDirectCompletionAction.Release,
            handshake.PublishOwner());
    }

    [Fact]
    public void Owner_before_completion_notifies_on_completion_publication()
    {
        var handshake = new PendingDirectCompletionHandshake();

        Assert.Equal(
            PendingDirectCompletionAction.None,
            handshake.PublishOwner());
        Assert.Equal(
            PendingDirectCompletionAction.Release,
            handshake.PublishCompletion(sendCancel: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Completion_and_owner_preserve_cancel_notification(bool completionFirst)
    {
        var handshake = new PendingDirectCompletionHandshake();

        var first = completionFirst
            ? handshake.PublishCompletion(sendCancel: true)
            : handshake.PublishOwner();
        var second = completionFirst
            ? handshake.PublishOwner()
            : handshake.PublishCompletion(sendCancel: true);

        Assert.Equal(PendingDirectCompletionAction.None, first);
        Assert.Equal(PendingDirectCompletionAction.ReleaseAndSendCancel, second);
    }

    [Fact]
    public void First_completion_publication_owns_cancel_decision()
    {
        var handshake = new PendingDirectCompletionHandshake();

        Assert.Equal(
            PendingDirectCompletionAction.None,
            handshake.PublishCompletion(sendCancel: false));
        Assert.Equal(
            PendingDirectCompletionAction.None,
            handshake.PublishCompletion(sendCancel: true));
        Assert.Equal(
            PendingDirectCompletionAction.Release,
            handshake.PublishOwner());
        Assert.Equal(
            PendingDirectCompletionAction.None,
            handshake.PublishOwner());
    }

    [Fact]
    public async Task Concurrent_publication_claims_exactly_one_notification()
    {
        const int iterations = 256;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var holder = new HandshakeHolder();
            using var start = new ManualResetEventSlim();
            var owner = Task.Run(() =>
            {
                start.Wait();
                return holder.Handshake.PublishOwner();
            });
            var completion = Task.Run(() =>
            {
                start.Wait();
                return holder.Handshake.PublishCompletion(sendCancel: true);
            });

            start.Set();
            var actions = await Task.WhenAll(owner, completion);

            Assert.Equal(1, actions.Count(action => action != PendingDirectCompletionAction.None));
            Assert.Contains(PendingDirectCompletionAction.ReleaseAndSendCancel, actions);
        }
    }

    private sealed class HandshakeHolder
    {
        public PendingDirectCompletionHandshake Handshake;
    }
}
