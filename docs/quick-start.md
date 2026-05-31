# ShaRPC Quick Start Guide

Get up and running with ShaRPC in 5 minutes.

## 1. Define Your Service Contract

Create a shared library with your service interface:

```csharp
// Shared/IMyService.cs
using ShaRPC.Core.Attributes;
using MessagePack;

[ShaRpcService]
public interface IMyService
{
    Task<GreetingResponse> GreetAsync(GreetingRequest request, CancellationToken ct = default);
}

[MessagePackObject]
public class GreetingRequest
{
    [Key(0)] public string Name { get; set; } = "";
}

[MessagePackObject]
public class GreetingResponse
{
    [Key(0)] public string Message { get; set; } = "";
    [Key(1)] public DateTime ServerTime { get; set; }
}
```

## 2. Implement the Server

```csharp
// Server/MyService.cs
public class MyService : IMyService
{
    public Task<GreetingResponse> GreetAsync(GreetingRequest request, CancellationToken ct)
    {
        return Task.FromResult(new GreetingResponse
        {
            Message = $"Hello, {request.Name}!",
            ServerTime = DateTime.UtcNow
        });
    }
}

// Server/Program.cs
using ShaRPC.Core.Server;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

var server = new ShaRpcServerBuilder()
    .UseTransport(new TcpServerTransport(5050))
    .UseSerializer(new MessagePackRpcSerializer())
    .AddMyService(new MyService())
    .Build();

await server.StartAsync();
Console.WriteLine("Server running on port 5050");
Console.ReadLine();
```

## 3. Create the Client

```csharp
// Client/Program.cs
using ShaRPC.Core.Client;
using ShaRPC.Generated;
using ShaRPC.Serializers.MessagePack;
using ShaRPC.Transports.Tcp;

var client = new ShaRpcClientBuilder()
    .UseTransport(new TcpTransport("localhost", 5050))
    .UseSerializer(new MessagePackRpcSerializer())
    .Build();

await client.ConnectAsync();

var service = client.CreateMyServiceProxy();
var response = await service.GreetAsync(new GreetingRequest { Name = "World" });

Console.WriteLine(response.Message);  // "Hello, World!"
Console.WriteLine(response.ServerTime);
```

## 4. Run It

```bash
# Terminal 1: Start server
dotnet run --project Server

# Terminal 2: Run client
dotnet run --project Client
```

## Project References

Your shared project needs these references:

```xml
<ItemGroup>
  <PackageReference Include="MessagePack" Version="2.5.187" />
  <ProjectReference Include="../ShaRPC.Core/ShaRPC.Core.csproj" />
  <ProjectReference Include="../ShaRPC.SourceGenerator/ShaRPC.SourceGenerator.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

Server and client projects reference:
- Your shared project
- `ShaRPC.Transports.Tcp`
- `ShaRPC.Serializers.MessagePack`

## What Gets Generated?

The source generator creates:

1. **Proxy** (`MyServiceProxy`) - Client-side stub that serializes calls
2. **Dispatcher** (`MyServiceDispatcher`) - Server-side router that deserializes and invokes
3. **Extensions** (`CreateMyServiceProxy()`, `AddMyService()`) - Convenience methods
4. **Registry factory** (`ShaRpcGenerated`) - Typed proxy/dispatcher factory backed by generated delegates
5. **Service catalog** (`ShaRpcGenerated.Services`) - Array-backed `ShaRpcGeneratedService` descriptors

You can use the generated factory directly when building framework-style APIs:

```csharp
using ShaRPC.Generated;

var proxy = ShaRpcGenerated.CreateProxy<IMyService>(client);
var dispatcher = ShaRpcGenerated.CreateDispatcher<IMyService>(new MyService());

foreach (var service in ShaRpcGenerated.Services)
{
    Console.WriteLine($"{service.ServiceType.Name}: {service.ProxyType.Name}");
}
```

## Next Steps

- [Unity Integration Guide](./unity-integration.md) - Full Unity setup
- [WebSocket Transport Guide](./websocket-setup.md) - WebSocket setup for browsers and WebGL
- [Generated Service Registry](./generated-service-registry.md) - Factory and registry usage
- [Samples](../samples/) - Working examples
- [API Reference](./api-reference.md) - Detailed API docs
