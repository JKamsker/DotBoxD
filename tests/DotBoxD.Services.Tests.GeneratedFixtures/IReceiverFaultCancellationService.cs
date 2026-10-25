using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.GeneratedFixtures;

[RpcService]
public interface IReceiverFaultCancellationService
{
    Task<int> FaultAsync(int value, CancellationToken ct = default);
}
