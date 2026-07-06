using Xunit;
using BufferPayload = DotBoxD.Services.Buffers.Payload;

namespace DotBoxD.Services.Tests.Protocol.Payload;

public sealed class PayloadRentContractTests
{
    [Fact]
    public void Rent_NegativeLength_ThrowsWithPublicParameterName()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => BufferPayload.Rent(-1));

        Assert.Equal("length", ex.ParamName);
    }
}
