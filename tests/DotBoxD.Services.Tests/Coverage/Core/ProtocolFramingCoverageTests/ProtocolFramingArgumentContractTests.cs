using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class ProtocolFramingArgumentContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(MessageFramer.HeaderSize - 1)]
    public void ValidateOutgoingFrame_WithMaximumBelowHeader_ThrowsArgumentOutOfRange(int maxMessageSize)
    {
        using var frame = MessageFramer.FrameToPayload(7, MessageType.Request, ReadOnlySpan<byte>.Empty);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => MessageFramer.ValidateOutgoingFrame(frame.Span, maxMessageSize));

        Assert.Equal("maxMessageSize", ex.ParamName);
    }
}
