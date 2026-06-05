using ShaRPC.Core.Protocol;
using ShaRPC.Core.Serialization;

namespace ShaRPC.Core.Streaming;

internal sealed class RpcOutboundStreamSet : IAsyncDisposable
{
    private readonly RpcStreamManager _manager;
    private readonly ISerializer _serializer;
    private readonly (RpcStreamAttachment Attachment, RpcStreamSendState State)[] _streams;
    private Task[]? _tasks;
    private int _started;

    public static RpcOutboundStreamSet Empty { get; } = new();

    private RpcOutboundStreamSet()
    {
        _manager = null!;
        _serializer = null!;
        _streams = Array.Empty<(RpcStreamAttachment, RpcStreamSendState)>();
        _tasks = Array.Empty<Task>();
        _started = 1;
    }

    public RpcOutboundStreamSet(
        RpcStreamManager manager,
        ISerializer serializer,
        (RpcStreamAttachment Attachment, RpcStreamSendState State)[] streams)
    {
        _manager = manager;
        _serializer = serializer;
        _streams = streams;
    }

    public bool IsEmpty => _streams.Length == 0;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0 || IsEmpty)
        {
            return;
        }

        var tasks = new Task[_streams.Length];
        for (var i = 0; i < _streams.Length; i++)
        {
            var pair = _streams[i];
            tasks[i] = Task.Run(() => PumpAsync(pair.Attachment, pair.State));
        }

        _tasks = tasks;
    }

    public async Task WaitAsync()
    {
        var tasks = _tasks;
        if (tasks is not { Length: > 0 })
        {
            return;
        }

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            foreach (var pair in _streams)
            {
                pair.State.Cancel();
            }

            var tasks = _tasks;
            if (tasks is { Length: > 0 })
            {
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            else if (Interlocked.Exchange(ref _started, 1) == 0)
            {
                foreach (var pair in _streams)
                {
                    try
                    {
                        await pair.Attachment.DisposeSourceAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        RpcDiagnostics.Report("Outbound stream source cleanup failed", ex);
                    }
                }
            }
        }
        finally
        {
            foreach (var pair in _streams)
            {
                _manager.RemoveOutbound(pair.State.StreamId);
            }
        }
    }

    private async Task PumpAsync(RpcStreamAttachment attachment, RpcStreamSendState state)
    {
        try
        {
            await attachment.PumpCoreAsync(_manager, _serializer, state.Token).ConfigureAwait(false);
            await _manager.SendStreamCompleteAsync(state.StreamId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (state.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Outbound stream pump failed", ex);
            try
            {
                await _manager.SendStreamErrorAsync(state.StreamId, ex, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception sendError)
            {
                RpcDiagnostics.Report("Outbound stream error notification failed", sendError);
            }
        }
        finally
        {
            _manager.RemoveOutbound(state.StreamId);
        }
    }
}
