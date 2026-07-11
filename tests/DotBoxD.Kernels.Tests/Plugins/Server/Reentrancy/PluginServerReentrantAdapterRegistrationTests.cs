using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins.Server.Reentrancy;

public sealed class PluginServerReentrantAdapterRegistrationTests
{
    [Fact]
    public void RegisterEventAdapter_rejects_reentrant_dispose_during_adapter_metadata_read()
    {
        using var server = PluginServer.Create();
        var adapter = new DisposingEventNameAdapter(server);

        var exception = Record.Exception(() => server.RegisterEventAdapter(adapter));
        var registered = server.Events.TryResolveErased(nameof(ReentrantEvent), out _);

        Assert.True(
            exception is ObjectDisposedException,
            "Expected ObjectDisposedException after adapter metadata disposed the server, " +
            $"but got {exception?.GetType().Name ?? "no exception"}; adapter registered: {registered}.");
        Assert.False(registered);
    }

    private sealed record ReentrantEvent(string Value);

    private sealed class DisposingEventNameAdapter(PluginServer server) : IPluginEventAdapter<ReentrantEvent>
    {
        public string EventName
        {
            get
            {
                server.Dispose();
                return nameof(ReentrantEvent);
            }
        }

        public IReadOnlyList<Parameter> Parameters { get; } = [new("value", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ReentrantEvent e)
            => [SandboxValue.FromString(e.Value)];
    }
}
