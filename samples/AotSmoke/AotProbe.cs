using DotBoxD.Services.Attributes;

namespace DotBoxD.AotSmoke;

[RpcService]
public interface IAotProbe
{
    ValueTask<int> DoubleAsync(int value, CancellationToken cancellationToken = default);
}

public sealed class AotProbe : IAotProbe
{
    public ValueTask<int> DoubleAsync(int value, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(value * 2);
}
