using DotBoxD.AotSmoke;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Peer;
using DotBoxD.Services.Testing;
using DotBoxD.Transports.Tcp;
using MessagePack.Resolvers;

// Directly root the source-generated registry. NativeAOT cannot discover a generated
// type by its reflection-only name after trimming, so AOT hosts bootstrap it explicitly.
_ = DotBoxD.Services.Generated.DotBoxDGenerated.Services;

var (serverChannel, clientChannel) = InMemoryRpcChannel.CreatePair();
var serializer = MessagePackRpcSerializer.CreateWithResolver(BuiltinResolver.Instance);
await using var server = RpcPeer.Over(serverChannel, serializer).Provide<IAotProbe>(new AotProbe()).Start();
await using var client = RpcPeer.Over(clientChannel, serializer).Start();

var result = await client.Get<IAotProbe>().DoubleAsync(21);
using var sandbox = SandboxHost.Create(builder => builder.UseCompilerIfAvailable());
var span = new SourceSpan(1, 1);
var module = new SandboxModule(
    "aot-auto-fallback",
    SemVersion.One,
    SemVersion.One,
    [],
    [new SandboxFunction(
        "main",
        true,
        [],
        SandboxType.I32,
        [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(42), span), span)])],
    new Dictionary<string, string>());
var plan = await sandbox.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
var execution = await sandbox.ExecuteAsync(
    plan,
    "main",
    SandboxValue.Unit,
    new SandboxExecutionOptions { Mode = ExecutionMode.Auto });

if (result != 42 ||
    !execution.Succeeded ||
    execution.ActualMode != ExecutionMode.Interpreted ||
    typeof(TcpTransport).Assembly.GetName().Name != "DotBoxD.Transports.Tcp")
{
    return 1;
}

return 0;
