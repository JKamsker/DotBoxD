using System.Text;

namespace DotBoxD.Codecs.MessagePack;

internal enum RpcRequestNameKind
{
    Service,
    Method,
}

internal sealed class RpcRequestNameCache
{
    private const int MaxRegisteredEntries = 128;
    private const int MaxCachedUtf8Bytes = 256;
    private const int RemoteSetCount = 32;
    private const int RemoteWays = 4;
    private const int RemoteSetMask = RemoteSetCount - 1;

    // Locally serialized names are trusted and cannot be displaced by remote churn. Copy-on-write
    // indexes keep inbound lookups lock-free; their volatile registered-only hints are best effort.
    private readonly object _gate = new();
    private readonly RpcRequestNameEntry?[] _remoteEntries =
        new RpcRequestNameEntry[RemoteSetCount * RemoteWays];
    // A fixed two-hit admission filter avoids allocating entries and byte arrays for one-off names.
    private readonly ulong[] _remoteCandidates = new ulong[RemoteSetCount * RemoteWays];
    private readonly byte[] _remoteCandidateMasks = new byte[RemoteSetCount];
    private readonly byte[] _nextRemoteCandidates = new byte[RemoteSetCount];
    private readonly byte[] _nextRemoteWays = new byte[RemoteSetCount];
    private RpcRequestNameEntry? _hotMethod;
    private RpcRequestNameEntry? _hotService;
    private RegisteredRpcRequestNames _registeredNames = RegisteredRpcRequestNames.Empty;

    public byte[]? Register(string? value, RpcRequestNameKind kind)
    {
        if (value is null)
        {
            return null;
        }

        if (TryGetRegistered(value, kind, out var registeredUtf8))
        {
            return registeredUtf8;
        }

        var registered = Volatile.Read(ref _registeredNames);
        if (registered.Count >= MaxRegisteredEntries ||
            !CanCache(value))
        {
            return null;
        }

        Span<byte> utf8 = stackalloc byte[MaxCachedUtf8Bytes];
        var bytesWritten = Encoding.UTF8.GetBytes(value.AsSpan(), utf8);
        return AddRegistered(value, utf8[..bytesWritten], Hash(utf8[..bytesWritten]), kind);
    }

    public bool TryGetRegistered(string value, RpcRequestNameKind kind, out byte[]? utf8)
    {
        var registered = Volatile.Read(ref _registeredNames);
        if (registered.TryGet(value, kind, out var entry))
        {
            SetHot(kind, entry);
            utf8 = entry.Utf8;
            return true;
        }

        utf8 = null;
        return false;
    }

    public string GetOrAdd(ReadOnlySpan<byte> utf8, RpcRequestNameKind kind)
    {
        if (utf8.Length > MaxCachedUtf8Bytes)
        {
            return Encoding.UTF8.GetString(utf8);
        }

        var hot = ReadHot(kind);
        if (hot is not null && utf8.SequenceEqual(hot.Utf8))
        {
            return hot.Value;
        }

        var hash = Hash(utf8);
        var cached = Find(utf8, hash);
        if (cached is not null)
        {
            SetHot(kind, cached);
            return cached.Value;
        }

        return AddRemote(Encoding.UTF8.GetString(utf8), utf8, hash, kind);
    }

    public string GetOrAdd(string value, RpcRequestNameKind kind)
    {
        var registered = Volatile.Read(ref _registeredNames);
        if (registered.TryGet(value, kind, out var registeredEntry))
        {
            SetHot(kind, registeredEntry);
            return registeredEntry.Value;
        }

        var hot = ReadHot(kind);
        if (hot is not null && string.Equals(value, hot.Value, StringComparison.Ordinal))
        {
            return hot.Value;
        }

        if (!CanCache(value))
        {
            return value;
        }

        Span<byte> utf8 = stackalloc byte[MaxCachedUtf8Bytes];
        var bytesWritten = Encoding.UTF8.GetBytes(value.AsSpan(), utf8);
        var encoded = utf8[..bytesWritten];
        var hash = Hash(encoded);
        var cached = FindRemote(encoded, hash);
        if (cached is not null)
        {
            SetHot(kind, cached);
            return cached.Value;
        }

        return AddRemote(value, encoded, hash, kind);
    }

    private byte[]? AddRegistered(
        string value,
        ReadOnlySpan<byte> utf8,
        ulong hash,
        RpcRequestNameKind kind)
    {
        lock (_gate)
        {
            var registered = Volatile.Read(ref _registeredNames);
            if (registered.TryGet(value, kind, out var registeredEntry))
            {
                SetHot(kind, registeredEntry);
                return registeredEntry.Utf8;
            }

            if (registered.Count >= MaxRegisteredEntries)
            {
                return null;
            }

            var entry = new RpcRequestNameEntry(value, utf8.ToArray(), hash);
            SetHot(kind, entry);
            Volatile.Write(ref _registeredNames, registered.Add(entry));
            return entry.Utf8;
        }
    }

    private string AddRemote(
        string value,
        ReadOnlySpan<byte> utf8,
        ulong hash,
        RpcRequestNameKind kind)
    {
        lock (_gate)
        {
            var cached = Find(utf8, hash);
            if (cached is not null)
            {
                SetHot(kind, cached);
                return cached.Value;
            }

            var set = GetRemoteSet(hash);
            var setStart = set * RemoteWays;
            var way = FindEmptyRemoteWay(setStart);
            if (way < 0)
            {
                // A repeated candidate earns a bounded slot; unrelated churn only rotates hashes.
                var candidateWay = FindRemoteCandidate(set, hash);
                if (candidateWay < 0)
                {
                    candidateWay = _nextRemoteCandidates[set];
                    _nextRemoteCandidates[set] = (byte)((candidateWay + 1) % RemoteWays);
                    _remoteCandidates[setStart + candidateWay] = hash;
                    _remoteCandidateMasks[set] |= (byte)(1 << candidateWay);
                    return value;
                }

                _remoteCandidateMasks[set] &= (byte)~(1 << candidateWay);
                way = _nextRemoteWays[set];
            }

            _nextRemoteWays[set] = (byte)((way + 1) % RemoteWays);
            var entry = new RpcRequestNameEntry(value, utf8.ToArray(), hash);
            Volatile.Write(ref _remoteEntries[setStart + way], entry);
            SetHot(kind, entry);
            return value;
        }
    }

    private RpcRequestNameEntry? Find(ReadOnlySpan<byte> utf8, ulong hash)
    {
        var registered = Volatile.Read(ref _registeredNames);
        if (registered.ByHash.TryGetValue(hash, out var entries))
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (utf8.SequenceEqual(entries[i].Utf8))
                {
                    return entries[i];
                }
            }
        }

        return FindRemote(utf8, hash);
    }

    private RpcRequestNameEntry? FindRemote(ReadOnlySpan<byte> utf8, ulong hash)
    {
        var setStart = GetRemoteSet(hash) * RemoteWays;
        for (var i = 0; i < RemoteWays; i++)
        {
            var entry = Volatile.Read(ref _remoteEntries[setStart + i]);
            if (entry is not null && entry.Hash == hash && utf8.SequenceEqual(entry.Utf8))
            {
                return entry;
            }
        }

        return null;
    }

    private RpcRequestNameEntry? ReadHot(RpcRequestNameKind kind) =>
        kind == RpcRequestNameKind.Service
            ? Volatile.Read(ref _hotService)
            : Volatile.Read(ref _hotMethod);

    private void SetHot(RpcRequestNameKind kind, RpcRequestNameEntry entry)
    {
        if (kind == RpcRequestNameKind.Service)
        {
            Volatile.Write(ref _hotService, entry);
        }
        else
        {
            Volatile.Write(ref _hotMethod, entry);
        }
    }

    private int FindEmptyRemoteWay(int setStart)
    {
        for (var i = 0; i < RemoteWays; i++)
        {
            if (Volatile.Read(ref _remoteEntries[setStart + i]) is null)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindRemoteCandidate(int set, ulong hash)
    {
        var setStart = set * RemoteWays;
        var occupied = _remoteCandidateMasks[set];
        for (var i = 0; i < RemoteWays; i++)
        {
            if ((occupied & (1 << i)) != 0 && _remoteCandidates[setStart + i] == hash)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetRemoteSet(ulong hash) =>
        (int)((hash ^ (hash >> 32)) & RemoteSetMask);

    private static bool CanCache(string value) =>
        value.Length <= MaxCachedUtf8Bytes &&
        Encoding.UTF8.GetByteCount(value) <= MaxCachedUtf8Bytes;

    private static ulong Hash(ReadOnlySpan<byte> utf8)
    {
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offsetBasis;
        for (var i = 0; i < utf8.Length; i++)
        {
            hash ^= utf8[i];
            hash *= prime;
        }

        return hash;
    }
}
