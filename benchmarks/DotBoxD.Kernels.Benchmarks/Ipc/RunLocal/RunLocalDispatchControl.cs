namespace DotBoxD.Kernels.Benchmarks.Ipc.RunLocal;

using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Keeps the raw generated decoder and synchronous terminal identical on both sides of the registry boundary,
/// so the direct lane is a negative control for dispatch-wrapper changes.
/// </summary>
internal sealed class RunLocalDispatchControl
{
    private const string SubscriptionId = "runlocal-dispatch-control";
    private const int ProjectedValue = 42;

    private readonly RemoteLocalHandlerRegistry _registry = new();
    private readonly HookContext _context = new(new InMemoryPluginMessageSink(), CancellationToken.None);
    private readonly byte[] _payload = KernelRpcBinaryCodec.EncodeValue(SandboxValue.FromInt32(ProjectedValue));

    public RunLocalDispatchControl()
        => _registry.Register<int>(SubscriptionId, AccumulateAsync, Decode);

    public long Checksum { get; private set; }

    public ValueTask DispatchAsync()
        => _registry.DispatchAsync(SubscriptionId, _payload, _context);

    public ValueTask InvokeDirectAsync()
        => AccumulateAsync(Decode(_payload), _context);

    public static long ExpectedChecksum(int iterations)
        => (long)ProjectedValue * iterations;

    private ValueTask AccumulateAsync(int value, HookContext _)
    {
        Checksum += value;
        return ValueTask.CompletedTask;
    }

    private static int Decode(ReadOnlyMemory<byte> payload)
    {
        var reader = new KernelRpcPayloadReader(payload.Span);
        var value = reader.ReadInt32();
        reader.EnsureConsumed();
        return value;
    }
}
