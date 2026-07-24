using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.TcpPooling;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TcpFrameSendOperationCollection
{
    public const string Name = "TCP frame send operation population";
}
