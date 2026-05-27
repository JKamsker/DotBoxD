using Server;
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const int Port = 5050;

Console.WriteLine("ShaRPC Server Example");
Console.WriteLine("=====================");
Console.WriteLine();

var transport = new TcpServerTransport(Port);
var serializer = new MessagePackRpcSerializer();
var gameService = new GameService();

var server = new ShaRpcServerBuilder()
    .UseTransport(transport)
    .UseSerializer(serializer)
    .AddGameService(gameService)
    .Build();

Console.WriteLine($"Starting server on port {Port}...");
await server.StartAsync();
Console.WriteLine("Server started. Press Ctrl+C to stop.");

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\nShutting down...");
}

await server.StopAsync();
await server.DisposeAsync();
Console.WriteLine("Server stopped.");
