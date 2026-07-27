using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.GeneratedFixtures;

[RpcService]
public interface ISubServiceLifecycleRoot
{
    Task<ISubServiceLifecycleChild> CreateAsync(CancellationToken ct = default);
}

[RpcService]
public interface ISubServiceLifecycleChild : IAsyncDisposable
{
    Task<int> PingAsync(CancellationToken ct = default);
}

[RpcService]
public interface ISubServiceDisposableRoot
{
    Task<ISubServiceDisposableChild> CreateAsync(CancellationToken ct = default);
}

[RpcService]
public interface ISubServiceDisposableChild : IDisposable
{
    Task<int> PingAsync(CancellationToken ct = default);
}
