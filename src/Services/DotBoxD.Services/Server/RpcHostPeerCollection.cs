using System.Collections.Concurrent;
using DotBoxD.Services.Peer;

namespace DotBoxD.Services.Server;

internal sealed class RpcHostPeerCollection
{
    private readonly ConcurrentDictionary<RpcPeer, RpcHostPeerAdmission.RpcHostPeerAdmissionLease> _peers = new();
    private readonly ConcurrentDictionary<Task, byte> _cleanupTasks = new();

    public bool TryAdd(RpcPeer peer, RpcHostPeerAdmission.RpcHostPeerAdmissionLease admission) =>
        _peers.TryAdd(peer, admission);

    public void Remove(RpcPeer peer)
    {
        if (_peers.TryRemove(peer, out var admission))
        {
            admission.Dispose();
        }
    }

    public void DisposeInBackground(RpcPeer peer)
    {
        var cleanup = BeginCleanup();
        CompleteCleanupInBackground(peer, cleanup);
    }

    public PendingCleanup BeginCleanup()
    {
        var cleanup = new PendingCleanup();
        _cleanupTasks.TryAdd(cleanup.Task, 0);
        _ = cleanup.Task.ContinueWith(
            static (task, state) =>
                ((ConcurrentDictionary<Task, byte>)state!).TryRemove(task, out _),
            _cleanupTasks,
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        return cleanup;
    }

    public void CompleteCleanupInBackground(RpcPeer peer, PendingCleanup cleanup)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await peer.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup.
            }
            finally
            {
                cleanup.SetResult();
            }
        });
    }

    public async Task CloseAllAsync()
    {
        // A peer that disconnects naturally just before this runs may be disposed twice:
        // once by DisposeInBackground and once here. RpcPeer.DisposeAsync is idempotent.
        var tasks = _peers.Keys.Select(peer => DisposeOnePeerAsync(peer)).ToArray();
        if (tasks.Length != 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        ReleaseAll();
    }

    private void ReleaseAll()
    {
        foreach (var pair in _peers)
        {
            if (_peers.TryRemove(pair.Key, out var admission))
            {
                admission.Dispose();
            }
        }
    }

    private static async Task DisposeOnePeerAsync(RpcPeer peer)
    {
        try
        {
            await peer.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public async Task AwaitCleanupAsync()
    {
        var tasks = _cleanupTasks.Keys.ToArray();
        if (tasks.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            // Peer cleanup is best-effort and each task observes its own dispose failures.
        }
    }

    internal sealed class PendingCleanup
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _completion.Task;

        public void SetResult() => _completion.TrySetResult(true);
    }
}
