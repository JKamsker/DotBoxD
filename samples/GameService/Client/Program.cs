using Shared;
using ShaRPC.Core.Client;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

const string Host = "localhost";
const int Port = 5050;

Console.WriteLine("ShaRPC Client Example");
Console.WriteLine("=====================");
Console.WriteLine();

var transport = new TcpTransport(Host, Port);
var serializer = new MessagePackRpcSerializer();

var client = new ShaRpcClientBuilder()
    .UseTransport(transport)
    .UseSerializer(serializer)
    .WithTimeout(TimeSpan.FromSeconds(10))
    .Build();

try
{
    Console.WriteLine($"Connecting to {Host}:{Port}...");
    await client.ConnectAsync();
    Console.WriteLine("Connected!");
    Console.WriteLine();

    var gameService = client.CreateGameServiceProxy();

    Console.WriteLine("Getting server status...");
    var status = await gameService.GetServerStatusAsync();
    Console.WriteLine($"  Players online: {status.PlayerCount}");
    Console.WriteLine($"  Server time: {status.ServerTime}");
    Console.WriteLine($"  Version: {status.Version}");
    Console.WriteLine();

    Console.WriteLine("Registering new player...");
    var playerState = await gameService.RegisterPlayerAsync("TestPlayer");
    Console.WriteLine($"  Player ID: {playerState.PlayerId}");
    Console.WriteLine($"  Name: {playerState.Name}");
    Console.WriteLine($"  Level: {playerState.Level}");
    Console.WriteLine($"  Health: {playerState.Health}/{playerState.MaxHealth}");
    Console.WriteLine($"  Position: ({playerState.PositionX}, {playerState.PositionY}, {playerState.PositionZ})");
    Console.WriteLine();

    Console.WriteLine("Moving player...");
    var moveResult = await gameService.MovePlayerAsync(new MoveRequest
    {
        PlayerId = playerState.PlayerId,
        X = 10.5f,
        Y = 0,
        Z = 20.3f
    });
    Console.WriteLine($"  Success: {moveResult.Success}");
    Console.WriteLine($"  Message: {moveResult.Message}");
    Console.WriteLine();

    Console.WriteLine("Getting updated player state...");
    var updatedState = await gameService.GetPlayerStateAsync(new PlayerId { Id = playerState.PlayerId });
    Console.WriteLine($"  Position: ({updatedState.PositionX}, {updatedState.PositionY}, {updatedState.PositionZ})");
    Console.WriteLine();

    Console.WriteLine("Performing action...");
    var actionResult = await gameService.PerformActionAsync(new ActionRequest
    {
        PlayerId = playerState.PlayerId,
        ActionType = "Attack",
        TargetId = "enemy_1"
    });
    Console.WriteLine($"  Success: {actionResult.Success}");
    Console.WriteLine($"  Message: {actionResult.Message}");
    Console.WriteLine();

    // Get final server status
    Console.WriteLine("Final server status...");
    status = await gameService.GetServerStatusAsync();
    Console.WriteLine($"  Players online: {status.PlayerCount}");
    Console.WriteLine();

    Console.WriteLine("All RPC calls completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine(ex.ToString());
}
finally
{
    await client.DisposeAsync();
    Console.WriteLine("\nClient disconnected.");
}
