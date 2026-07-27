using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StreamSendOperationCollection
{
    public const string Name = "Stream send operation population";
}
