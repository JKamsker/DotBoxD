using System.Text.Json;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugValueCodecTests
{
    [Fact]
    public void Every_sandbox_value_shape_round_trips_through_the_debug_wire_shape()
    {
        var values = new SandboxValue[]
        {
            SandboxValue.Unit,
            SandboxValue.FromBool(true),
            SandboxValue.FromInt32(42),
            SandboxValue.FromInt64(4_000_000_000),
            SandboxValue.FromDouble(1.25),
            SandboxValue.FromString("debug"),
            SandboxValue.FromGuid(Guid.Parse("52e28f85-b64d-4684-a559-62c454accb1f")),
            SandboxValue.FromOpaqueId("PlayerId", "player-1"),
            SandboxValue.FromPath("config/plugin.json"),
            SandboxValue.FromUri("https://example.test/debug"),
            SandboxValue.FromList([SandboxValue.FromInt32(1), SandboxValue.FromInt32(2)], SandboxType.I32),
            SandboxValue.FromRecord([SandboxValue.FromString("record"), SandboxValue.FromBool(false)]),
            SandboxValue.FromMap(
                new Dictionary<SandboxValue, SandboxValue>
                {
                    [SandboxValue.FromString("second")] = SandboxValue.FromInt64(2),
                    [SandboxValue.FromString("first")] = SandboxValue.FromInt64(1)
                },
                SandboxType.String,
                SandboxType.I64)
        };

        foreach (var original in values)
        {
            var snapshot = PluginDebugValueCodec.Snapshot(original);
            var encoded = JsonSerializer.SerializeToElement(snapshot);

            Assert.True(
                PluginDebugValueCodec.TryParse(encoded, original.Type, out var parsed, out var error),
                error);
            Assert.Equal(
                JsonSerializer.Serialize(snapshot),
                JsonSerializer.Serialize(PluginDebugValueCodec.Snapshot(parsed!)));
        }
    }

    [Fact]
    public void Invalid_debug_value_shapes_fail_closed_with_specific_errors()
    {
        AssertInvalid("42", SandboxType.I32, "JSON objects");
        AssertInvalid("{}", SandboxType.I32, "value is required");
        AssertInvalid("{\"value\":null}", SandboxType.String, "String value is null");
        AssertInvalid("{\"value\":\"not-a-guid\"}", SandboxType.Guid, "Guid");
        AssertInvalid("{\"value\":\"../secret\"}", SandboxType.SandboxPath, "portable relative paths");
        AssertInvalid("{\"children\":{}}", SandboxType.List(SandboxType.I32), "must be an array");
        AssertInvalid("{\"children\":[]}", new SandboxType("List", []), "List type is malformed");
        AssertInvalid(
            "{\"children\":[]}",
            SandboxType.Record([SandboxType.I32]),
            "field count");
        AssertInvalid("{\"entries\":[]}", new SandboxType("Map", [SandboxType.String]), "Map type is malformed");
        AssertInvalid("{\"value\":\"future\"}", new SandboxType("Future", [SandboxType.I32]), "Unsupported");
        AssertInvalid(
            """
            {"entries":[
              {"key":{"value":"same"},"value":{"value":1}},
              {"key":{"value":"same"},"value":{"value":2}}
            ]}
            """,
            SandboxType.Map(SandboxType.String, SandboxType.I32),
            "duplicate keys");
    }

    [Fact]
    public void Unknown_runtime_value_types_are_never_serialized()
    {
        var exception = Assert.Throws<NotSupportedException>(() =>
            PluginDebugValueCodec.Snapshot(new UnsupportedValue()));

        Assert.Contains(nameof(UnsupportedValue), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_debug_paths_decode_list_record_and_map_segments_with_their_original_types()
    {
        var type = SandboxType.List(SandboxType.Record([
            SandboxType.Map(SandboxType.String, SandboxType.I64)
        ]));
        var payload = JsonSerializer.Deserialize<JsonElement>(
            """
            {"path":[
              {"kind":"list","index":2},
              {"kind":"record","index":0},
              {"kind":"map","key":{"type":"String","value":"score"}}
            ]}
            """);

        var path = PluginDebugValuePathParser.Parse(payload, type);

        Assert.Equal(2, Assert.IsType<SandboxDebugListIndex>(path[0]).Index);
        Assert.Equal(0, Assert.IsType<SandboxDebugRecordField>(path[1]).Index);
        Assert.Equal(
            SandboxValue.FromString("score"),
            Assert.IsType<SandboxDebugMapValue>(path[2]).Key);
    }

    private static void AssertInvalid(string json, SandboxType type, string expectedError)
    {
        var encoded = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.False(PluginDebugValueCodec.TryParse(encoded, type, out var value, out var error));
        Assert.Null(value);
        Assert.Contains(expectedError, error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record UnsupportedValue : SandboxValue
    {
        public override SandboxType Type => SandboxType.Scalar("UnsupportedValue");
    }
}
