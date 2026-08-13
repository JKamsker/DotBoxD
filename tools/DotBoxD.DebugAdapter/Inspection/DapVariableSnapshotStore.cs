using System.Collections.Concurrent;
using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.DebugAdapter;

internal sealed class DapVariableSnapshotStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _snapshots = new(StringComparer.Ordinal);

    public async ValueTask<JsonElement> GetAsync(
        BridgeClient bridge,
        DapFrameContext context,
        CancellationToken cancellationToken)
    {
        if (_snapshots.TryGetValue(context.RemoteFrameId, out var snapshot))
        {
            return snapshot;
        }

        var variables = await bridge.RemoteAsync(
                PluginDebugCommands.Variables,
                new { frameId = context.RemoteFrameId },
                cancellationToken)
            .ConfigureAwait(false);
        _snapshots[context.RemoteFrameId] = variables;
        return variables;
    }

    public void Remove(string frameId) => _snapshots.TryRemove(frameId, out _);

    public void Remove(IEnumerable<string> frameIds)
    {
        foreach (var frameId in frameIds)
        {
            Remove(frameId);
        }
    }

    public void Clear() => _snapshots.Clear();
}
