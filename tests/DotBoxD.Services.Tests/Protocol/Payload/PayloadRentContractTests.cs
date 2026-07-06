using DotBoxD.Services.Buffers;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol;

public sealed class PayloadRentContractTests
{
    [Fact]
    public void Rent_NegativeLength_ThrowsWithPublicParameterName()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Payload.Rent(-1));

        Assert.Equal("length", ex.ParamName);
    }
}
