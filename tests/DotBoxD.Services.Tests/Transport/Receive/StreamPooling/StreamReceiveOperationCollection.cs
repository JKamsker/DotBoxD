using Xunit;

namespace DotBoxD.Services.Tests.Transport.Receive.StreamPooling;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StreamReceiveOperationCollection
{
    public const string Name = "Stream receive operation population";
}
