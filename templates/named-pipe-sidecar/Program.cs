using DotBoxD.Pushdown.Services;

var pipeName = "dotboxd-sidecar-" + Guid.NewGuid().ToString("N");
await using var host = RpcMessagePackIpc.ListenNamedPipe(pipeName, _ => { });
await host.StartAsync();
Console.WriteLine($"Listening on trusted-local pipe {pipeName}. Press Enter to stop.");
Console.ReadLine();
