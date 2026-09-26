using System.IO.Pipes;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.Compatibility;

public sealed class RpcProtocolNegotiationTests
{
    [Fact]
    public void Default_offer_matches_golden_bytes()
    {
        const string golden = "44425831010001000100000001000000" +
            "0000000000000000000000000000000000000001" +
            "0000000000000000000000000000000000000000000000000000000000000000";
        Assert.Equal(golden, Convert.ToHexStringLower(RpcProtocolNegotiation.Encode(new RpcProtocolOffer())));
        Assert.Equal(new RpcProtocolOffer(), RpcProtocolNegotiation.Decode(Convert.FromHexString(golden)));
    }

    [Fact]
    public void Previous_unversioned_cancel_frame_remains_byte_compatible()
    {
        // Frozen before the connection preamble was introduced; v1 preserves the frame body ABI.
        byte[] previousReleaseFrame = [9, 0, 0, 0, 42, 0, 0, 0, 4];
        Assert.True(MessageFramer.TryReadFrameHeader(previousReleaseFrame, out var id, out var type));
        Assert.Equal(42, id);
        Assert.Equal(MessageType.Cancel, type);
        using var current = MessageFramer.FrameToPayload(id, type, []);
        Assert.Equal(previousReleaseFrame, current.Memory.ToArray());
        Assert.Throws<InvalidDataException>(() => MessageFramer.ValidateOutgoingFrame(
            RpcProtocolNegotiation.Encode(new RpcProtocolOffer())));
    }

    [Fact]
    public void Negotiation_is_symmetric_and_ignores_optional_unknown_features()
    {
        var older = new RpcProtocolOffer { SupportedFeatures = 1, MaximumFrameSize = 1024 };
        var newer = new RpcProtocolOffer { MaximumVersion = 2, SupportedFeatures = 3, RequiredFeatures = 1 };
        var result = RpcProtocolNegotiation.Negotiate(older, newer);
        Assert.Equal(new RpcProtocolAgreement(1, 1, 1, 1024), result);
        Assert.Equal(result, RpcProtocolNegotiation.Negotiate(newer, older));
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Negotiate(older, newer with { RequiredFeatures = 2 }));
    }

    [Fact]
    public void Incompatible_versions_codecs_and_contracts_fail_before_frames()
    {
        var offer = new RpcProtocolOffer();
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Negotiate(offer, offer with { MinimumVersion = 2, MaximumVersion = 2 }));
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Negotiate(offer, offer with { CodecId = 2 }));
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Negotiate(offer, offer with { ContractFingerprint = new string('a', 64) }));
        var fingerprint = offer with { ContractFingerprint = new string('a', 64) };
        Assert.Equal(fingerprint, RpcProtocolNegotiation.Decode(RpcProtocolNegotiation.Encode(fingerprint)));
    }

    [Fact]
    public void Malformed_preambles_fail_closed()
    {
        var bytes = RpcProtocolNegotiation.Encode(new RpcProtocolOffer());
        foreach (var offset in new[] { 0, 4, 10 })
        {
            var invalid = (byte[])bytes.Clone();
            invalid[offset] = 255;
            Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Decode(invalid));
        }
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Decode(bytes.AsSpan(1)));
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Encode(new RpcProtocolOffer { RequiredFeatures = 1 }));
        Assert.Throws<InvalidDataException>(() => RpcProtocolNegotiation.Encode(new RpcProtocolOffer { MaximumFrameSize = 8 }));
    }

    [Fact]
    public async Task Public_exchange_and_handwritten_exchange_produce_identical_channels()
    {
        var name = "dbx-negotiation-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accepting = server.WaitForConnectionAsync(deadline.Token);
        await client.ConnectAsync(deadline.Token);
        await accepting;
        var offer = new RpcProtocolOffer { MaximumFrameSize = 1024 };
        var automated = RpcProtocolNegotiation.ExchangeAsync(server, offer, cancellationToken: deadline.Token);
        await client.WriteAsync(RpcProtocolNegotiation.Encode(offer), deadline.Token);
        var bytes = new byte[RpcProtocolNegotiation.PreambleSize];
        await client.ReadExactlyAsync(bytes, deadline.Token);
        var handwritten = RpcProtocolNegotiation.Negotiate(offer, RpcProtocolNegotiation.Decode(bytes));
        Assert.Equal(handwritten, await automated);
        await using var sender = new StreamConnection(client, ownsStream: false, maxMessageSize: handwritten.MaximumFrameSize);
        await using var receiver = new StreamConnection(server, ownsStream: false, maxMessageSize: handwritten.MaximumFrameSize);
        using var frame = MessageFramer.FrameToPayload(1, MessageType.Cancel, []);
        await sender.SendAsync(frame.Memory, deadline.Token);
        using var received = await receiver.ReceiveAsync(deadline.Token);
        Assert.Equal(frame.Memory.ToArray(), received.Memory.ToArray());
    }

    [Fact]
    public async Task Truncated_exchange_is_rejected()
    {
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<EndOfStreamException>(() => RpcProtocolNegotiation.ExchangeAsync(stream, new RpcProtocolOffer()));
    }
}
