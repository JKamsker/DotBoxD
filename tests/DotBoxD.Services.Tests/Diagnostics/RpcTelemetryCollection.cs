using Xunit;

namespace DotBoxD.Services.Tests.Diagnostics;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RpcTelemetryCollection
{
    public const string Name = "RPC telemetry";
}
