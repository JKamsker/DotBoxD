using System.Collections.Concurrent;
using DotBoxD.Services.Transport;

namespace DotBoxD.Services.Server;

internal sealed class RpcHostAcceptLoop
{
    private static readonly TimeSpan AcceptErrorBackoff = TimeSpan.FromMilliseconds(50);
    private static readonly AsyncLocal<InFlightHandoff?> CurrentHandoff = new();

    private readonly IServerTransport _listener;
    private readonly Func<IRpcChannel, Task> _addPeerAsync;
    private readonly Action<Exception> _acceptError;
    private readonly ConcurrentDictionary<InFlightHandoff, byte> _inFlight = new();

    public RpcHostAcceptLoop(
        IServerTransport listener,
        Func<IRpcChannel, Task> addPeerAsync,
        Action<Exception> acceptError)
    {
        _listener = listener;
        _addPeerAsync = addPeerAsync;
        _acceptError = acceptError;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            IRpcChannel connection;
            try
            {
                connection = await _listener.AcceptAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _acceptError(ex);
                if (!await DelayAfterErrorAsync(ct).ConfigureAwait(false))
                {
                    break;
                }

                continue;
            }

            TrackHandoff(connection);
        }
    }

    /// <summary>
    /// Awaits every peer hand-off the loop has started. Call after the loop task completes so a
    /// connection accepted just before shutdown finishes registering (and is then disposed by the
    /// host's peer drain) instead of starting a peer the host never tears down.
    /// </summary>
    public async Task DrainInFlightAsync()
    {
        var current = CurrentHandoff.Value;
        var tasks = _inFlight.Keys
            .Where(handoff => !ReferenceEquals(handoff, current))
            .Select(handoff => handoff.Task)
            .ToArray();
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
            // Each hand-off observes its own failure; we only need them quiesced before peer drain.
        }
    }

    private void TrackHandoff(IRpcChannel connection)
    {
        var handoff = new InFlightHandoff();
        handoff.Task = Task.Run(() => RunHandoffAsync(handoff, connection));

        // Register before attaching the self-removal continuation so a hand-off that finishes
        // before TryAdd still gets removed by the continuation firing on the completed task.
        _inFlight.TryAdd(handoff, 0);
        _ = handoff.Task.ContinueWith(
            static (task, state) =>
            {
                var (inFlight, completed) =
                    ((ConcurrentDictionary<InFlightHandoff, byte>, InFlightHandoff))state!;
                inFlight.TryRemove(completed, out _);
            },
            (_inFlight, handoff),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunHandoffAsync(InFlightHandoff handoff, IRpcChannel connection)
    {
        var previous = CurrentHandoff.Value;
        CurrentHandoff.Value = handoff;
        try
        {
            await _addPeerAsync(connection).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _acceptError(ex);
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            CurrentHandoff.Value = previous;
        }
    }

    private static async Task<bool> DelayAfterErrorAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(AcceptErrorBackoff, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed class InFlightHandoff
    {
        public Task Task { get; set; } = Task.CompletedTask;
    }
}
