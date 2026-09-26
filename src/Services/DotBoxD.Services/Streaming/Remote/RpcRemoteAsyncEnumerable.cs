using DotBoxD.Services.Serialization;
using DotBoxD.Services.Streaming.Core;

namespace DotBoxD.Services.Streaming.Remote;

internal sealed class RpcRemoteAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    private RpcStreamReceiver? _receiver;
    private ISerializer? _serializer;
    private int _enumerated;

    public RpcRemoteAsyncEnumerable(RpcStreamReceiver receiver, ISerializer serializer)
    {
        _receiver = receiver;
        _serializer = serializer;
    }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _enumerated, 1) != 0)
        {
            throw new InvalidOperationException("A remote RPC stream can only be enumerated once.");
        }

        var enumerator = new Enumerator(_receiver!, _serializer!, cancellationToken);
        _receiver = null;
        _serializer = null;
        return enumerator;
    }

    private sealed class Enumerator : IAsyncEnumerator<T>
    {
        private RpcStreamReceiver? _receiver;
        private ISerializer? _serializer;
        private CancellationToken _ct;

        public Enumerator(
            RpcStreamReceiver receiver,
            ISerializer serializer,
            CancellationToken ct)
        {
            _receiver = receiver;
            _serializer = serializer;
            _ct = ct;
        }

        public T Current { get; private set; } = default!;

        public async ValueTask<bool> MoveNextAsync()
        {
            var receiver = Volatile.Read(ref _receiver);
            if (receiver is null)
            {
                return false;
            }

            var chunk = await receiver.ReadChunkAsync(_ct).ConfigureAwait(false);
            if (chunk is null)
            {
                Complete(cancelReceiver: false);
                return false;
            }

            var serializer = Volatile.Read(ref _serializer);
            var ct = _ct;
            try
            {
                if (Volatile.Read(ref _receiver) is null || serializer is null)
                {
                    chunk.DisposeWithoutCredit();
                    return false;
                }

                ct.ThrowIfCancellationRequested();
                var current = serializer.Deserialize<T>(chunk.Payload);
                ct.ThrowIfCancellationRequested();

                Current = current;
                // Order publication before checking disposal, so either the disposer clears
                // Current or this operation observes disposal and clears its late publication.
                Thread.MemoryBarrier();
                if (Volatile.Read(ref _receiver) is null)
                {
                    Current = default!;
                    chunk.DisposeWithoutCredit();
                    return false;
                }

                chunk.Dispose();
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                Complete(cancelReceiver: true);
                chunk.DisposeWithoutCredit();
                throw;
            }
            catch
            {
                chunk.Dispose();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            Complete(cancelReceiver: true);
            return default;
        }

        private void Complete(bool cancelReceiver)
        {
            var receiver = Interlocked.Exchange(ref _receiver, null);
            _serializer = null;
            _ct = default;
            Current = default!;
            if (cancelReceiver)
            {
                receiver?.Cancel();
            }
        }
    }
}
