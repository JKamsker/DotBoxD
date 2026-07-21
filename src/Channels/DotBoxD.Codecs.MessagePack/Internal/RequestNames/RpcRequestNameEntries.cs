namespace DotBoxD.Codecs.MessagePack;

internal sealed class RegisteredRpcRequestNames
{
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
