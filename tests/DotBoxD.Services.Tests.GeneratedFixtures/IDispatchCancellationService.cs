using DotBoxD.Services.Attributes;

namespace DotBoxD.Services.Tests.GeneratedFixtures;

[RpcService]
public interface IDispatchCancellationService
{
    int Record(int value);

    Task<int> RecordAfterCancelAsync(int value, CancellationToken ct = default);

    void CancelVoid(CancellationToken ct = default);

    Task CancelTaskAsync(CancellationToken ct = default);

    ValueTask CancelValueTaskAsync(CancellationToken ct = default);
}
