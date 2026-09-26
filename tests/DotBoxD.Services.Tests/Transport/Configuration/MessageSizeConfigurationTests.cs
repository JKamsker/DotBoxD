using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.NamedPipes;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Configuration;

public sealed class MessageSizeConfigurationTests
{
    [Theory]
    [InlineData(MessageFramer.MaxMessageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Stream_connection_rejects_maximum_above_protocol_limit(int maximum)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StreamConnection(Stream.Null, ownsStream: false, maxMessageSize: maximum));

        AssertMaximumError(error, maximum);
    }

    [Theory]
    [InlineData(MessageFramer.MaxMessageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Local_pipe_client_rejects_maximum_above_protocol_limit(int maximum)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NamedPipeClientTransport("size-configuration", maximum));

        AssertMaximumError(error, maximum);
    }

    [Theory]
    [InlineData(MessageFramer.MaxMessageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Explicit_server_pipe_client_rejects_maximum_above_protocol_limit(int maximum)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NamedPipeClientTransport(".", "size-configuration", maximum));

        AssertMaximumError(error, maximum);
    }

    [Theory]
    [InlineData(MessageFramer.MaxMessageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Pipe_server_rejects_maximum_above_protocol_limit(int maximum)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NamedPipeServerTransport("size-configuration", maxMessageSize: maximum));

        AssertMaximumError(error, maximum);
    }

    [Theory]
    [InlineData(MessageFramer.MaxMessageSize + 1)]
    [InlineData(int.MaxValue)]
    public void Outgoing_validation_rejects_maximum_above_protocol_limit(int maximum)
    {
        using var frame = MessageFramer.FrameToPayload(7, MessageType.Request, ReadOnlySpan<byte>.Empty);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MessageFramer.ValidateOutgoingFrame(frame.Span, maximum));

        AssertMaximumError(error, maximum);
    }

    [Theory]
    [InlineData(MessageFramer.HeaderSize)]
    [InlineData(MessageFramer.HeaderSize + 1)]
    [InlineData(MessageFramer.MaxMessageSize)]
    public async Task Supported_maximum_is_accepted_by_transports_and_outgoing_validation(int maximum)
    {
        await using var connection = new StreamConnection(Stream.Null, ownsStream: false, maxMessageSize: maximum);
        await using var localClient = new NamedPipeClientTransport("size-configuration", maximum);
        await using var explicitClient = new NamedPipeClientTransport(".", "size-configuration", maximum);
        await using var server = new NamedPipeServerTransport("size-configuration", maxMessageSize: maximum);
        using var frame = MessageFramer.FrameToPayload(7, MessageType.Request, ReadOnlySpan<byte>.Empty);

        MessageFramer.ValidateOutgoingFrame(frame.Span, maximum);
    }

    private static void AssertMaximumError(ArgumentOutOfRangeException error, int maximum)
    {
        Assert.Equal("maxMessageSize", error.ParamName);
        Assert.Equal(maximum, error.ActualValue);
    }
}
