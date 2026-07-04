using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.GeneratedFixtures;

[DotBoxDService]
public interface ISubServiceLifecycleRoot
{
    Task<ISubServiceLifecycleChild> CreateAsync(CancellationToken ct = default);
}

[DotBoxDService]
public interface ISubServiceLifecycleChild : IAsyncDisposable
{
    Task<int> PingAsync(CancellationToken ct = default);
}
