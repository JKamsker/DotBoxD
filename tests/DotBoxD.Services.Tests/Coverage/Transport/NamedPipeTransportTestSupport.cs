namespace DotBoxD.Services.Tests.Coverage.Transport;

internal static class NamedPipeTransportTestSupport
{
    internal static readonly TimeSpan TransportTimeout = TimeSpan.FromSeconds(10);

    internal static string CreatePipeName() => "dotboxd-test-" + Guid.NewGuid().ToString("N");
}
