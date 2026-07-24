using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.TcpPooling;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TcpReceiveOperationCollection
{
    public const string Name = "TCP receive operation population";
}
