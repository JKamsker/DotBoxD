namespace DotBoxD.Codecs.MessagePack;

internal sealed class RegisteredRpcRequestNames
{
    // These hints can only point into the local registration indexes below. Keeping them separate
    // from the remote-name hints prevents rejected inbound names from bypassing later validation.
    private RpcRequestNameEntry? _hotMethod;
    private RpcRequestNameEntry? _hotService;

    public static RegisteredRpcRequestNames Empty { get; } = new(
        0,
        new Dictionary<string, RpcRequestNameEntry>(StringComparer.Ordinal),
        new Dictionary<ulong, RpcRequestNameEntry[]>());

    public RegisteredRpcRequestNames(
        int count,
        Dictionary<string, RpcRequestNameEntry> byValue,
        Dictionary<ulong, RpcRequestNameEntry[]> byHash)
    {
        Count = count;
        ByValue = byValue;
        ByHash = byHash;
    }

    public int Count { get; }

    public Dictionary<string, RpcRequestNameEntry> ByValue { get; }

    public Dictionary<ulong, RpcRequestNameEntry[]> ByHash { get; }

    public bool TryGet(
        string value,
        RpcRequestNameKind kind,
        out RpcRequestNameEntry entry)
    {
        var hot = kind == RpcRequestNameKind.Service
            ? Volatile.Read(ref _hotService)
            : Volatile.Read(ref _hotMethod);
        if (hot is not null && ReferenceEquals(value, hot.Value))
        {
            entry = hot;
            return true;
        }

        if (ByValue.TryGetValue(value, out entry))
        {
            SetHot(kind, entry);
            return true;
        }

        entry = null!;
        return false;
    }

    public RegisteredRpcRequestNames Add(RpcRequestNameEntry entry)
    {
        var byValue = new Dictionary<string, RpcRequestNameEntry>(ByValue, StringComparer.Ordinal)
        {
            [entry.Value] = entry,
        };
        var byHash = new Dictionary<ulong, RpcRequestNameEntry[]>(ByHash);
        if (byHash.TryGetValue(entry.Hash, out var collisions))
        {
            var updated = new RpcRequestNameEntry[collisions.Length + 1];
            Array.Copy(collisions, updated, collisions.Length);
            updated[collisions.Length] = entry;
            byHash[entry.Hash] = updated;
        }
        else
        {
            byHash.Add(entry.Hash, [entry]);
        }

        return new RegisteredRpcRequestNames(Count + 1, byValue, byHash);
    }

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
}

internal sealed class RpcRequestNameEntry
{
    public RpcRequestNameEntry(string value, byte[] utf8, ulong hash)
    {
        Value = value;
        Utf8 = utf8;
        Hash = hash;
    }

    public string Value { get; }

    public byte[] Utf8 { get; }

    public ulong Hash { get; }
}
