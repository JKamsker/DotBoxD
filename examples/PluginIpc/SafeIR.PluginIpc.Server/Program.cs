using SafeIR.PluginIpc.Server;
using ShaRPC.Core;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.NamedPipes;

var pipeName = args.Length > 0 ? args[0] : "safe-ir-plugin-ipc";
var service = await PluginControlService.CreateAsync();

await using var host = RpcHost
    .Listen(new NamedPipeServerTransport(pipeName), new MessagePackRpcSerializer())
    .ForEachPeer(peer => peer.ProvidePluginControlService(service));

await host.StartAsync();
Console.WriteLine($"SafeIR plugin IPC server listening on pipe '{pipeName}'.");
Console.WriteLine("Press Ctrl+C to stop.");

var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    stopped.TrySetResult();
};

await stopped.Task;
await host.StopAsync();
