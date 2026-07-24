namespace DotBoxD.Services.Client;

internal enum PendingDirectCompletionAction
{
    None,
    Release,
    ReleaseAndSendCancel,
}

// A synchronously sent Task-backed request publishes its direct owner independently from the read
// loop publishing the terminal response. The second publication claims the one owner notification
// in the same CAS, so neither publication can observe the other as absent and then lose the wakeup.
internal struct PendingDirectCompletionHandshake
{
    private const int OwnerPublished = 1 << 0;
    private const int CompletionPublished = 1 << 1;
    private const int NotificationClaimed = 1 << 2;
    private const int SendCancel = 1 << 3;
    private const int RequiredPublications = OwnerPublished | CompletionPublished;

    private int _state;

    public PendingDirectCompletionAction PublishOwner() =>
        Publish(OwnerPublished);

    // The first terminal producer owns whether the remote should receive a cancel frame. PendingRequests
    // removes the entry before invoking that producer, so later duplicate publications are no-ops.
    public PendingDirectCompletionAction PublishCompletion(bool sendCancel) =>
        Publish(CompletionPublished | (sendCancel ? SendCancel : 0));

    private PendingDirectCompletionAction Publish(int publication)
    {
        var publicationKind = publication & RequiredPublications;
        while (true)
        {
            var state = Volatile.Read(ref _state);
            if ((state & publicationKind) != 0)
            {
                return PendingDirectCompletionAction.None;
            }

            var next = state | publication;
            var notify = (next & RequiredPublications) == RequiredPublications &&
                (next & NotificationClaimed) == 0;
            if (notify)
            {
                next |= NotificationClaimed;
            }

            if (Interlocked.CompareExchange(ref _state, next, state) != state)
            {
                continue;
            }

            if (!notify)
            {
                return PendingDirectCompletionAction.None;
            }

            return (next & SendCancel) != 0
                ? PendingDirectCompletionAction.ReleaseAndSendCancel
                : PendingDirectCompletionAction.Release;
        }
    }
}
