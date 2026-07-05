namespace DotBoxD.Services.Server;

internal sealed class RpcHostPeerAdmission
{
    private readonly int? _limit;
    private int _count;

    public RpcHostPeerAdmission(int? limit)
    {
        if (limit is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        _limit = limit;
    }

    public bool TryAcquire(out RpcHostPeerAdmissionLease lease)
    {
        if (_limit is not { } limit)
        {
            lease = new RpcHostPeerAdmissionLease(owner: null);
            return true;
        }

        while (true)
        {
            var current = Volatile.Read(ref _count);
            if (current >= limit)
            {
                lease = null!;
                return false;
            }

            if (Interlocked.CompareExchange(ref _count, current + 1, current) == current)
            {
                lease = new RpcHostPeerAdmissionLease(this);
                return true;
            }
        }
    }

    private void Release() => Interlocked.Decrement(ref _count);

    internal sealed class RpcHostPeerAdmissionLease : IDisposable
    {
        private RpcHostPeerAdmission? _owner;

        public RpcHostPeerAdmissionLease(RpcHostPeerAdmission? owner) => _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Release();
        }
    }
}
