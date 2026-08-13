using System.Text;
using System.Text.Json;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging;

public sealed class PluginDebugProtocolTests
{
    [Fact]
    public void Envelope_round_trips_as_utf8_json()
    {
        var payload = JsonSerializer.SerializeToElement(new { command = "continue" });
        var original = new PluginDebugEnvelope(1, "request", "42", "secret", payload);

        var encoded = PluginDebugProtocol.Encode(original, 4096);
        var decoded = PluginDebugProtocol.Decode(encoded, 4096);

        Assert.Equal(original.Version, decoded.Version);
        Assert.Equal(original.Kind, decoded.Kind);
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.SessionToken, decoded.SessionToken);
        Assert.Equal("continue", decoded.Payload.GetProperty("command").GetString());
    }

    [Fact]
    public void Oversized_messages_are_rejected_before_json_parsing()
    {
        var message = Encoding.UTF8.GetBytes("{not-json-but-over-limit}");

        var error = Assert.Throws<PluginDebugProtocolException>(() => PluginDebugProtocol.Decode(message, 4));

        Assert.Equal("messageTooLarge", error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{\"version\":1}")]
    public void Malformed_envelopes_fail_closed(string message)
    {
        var error = Assert.Throws<PluginDebugProtocolException>(
            () => PluginDebugProtocol.Decode(Encoding.UTF8.GetBytes(message), 4096));

        Assert.Equal("invalidMessage", error.Code);
    }

    [Fact]
    public void Invalid_utf8_is_rejected()
    {
        var error = Assert.Throws<PluginDebugProtocolException>(
            () => PluginDebugProtocol.Decode(new byte[] { 0xff, 0xfe }, 4096));

        Assert.Equal("invalidMessage", error.Code);
    }

    [Fact]
    public void Duplicate_security_fields_are_rejected()
    {
        const string message = """
            {"version":1,"kind":"request","id":"1","sessionToken":"first","sessionToken":"second","payload":{}}
            """;

        var error = Assert.Throws<PluginDebugProtocolException>(
            () => PluginDebugProtocol.Decode(Encoding.UTF8.GetBytes(message), 4096));

        Assert.Equal("invalidMessage", error.Code);
    }
}
