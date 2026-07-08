namespace DotBoxD.Kernels.Tests.Plugins.Messaging;

public sealed class PluginMessageSinkContractTests
{
    public static TheoryData<string> InvalidTargetIds => new()
    {
        "",
        "   ",
        "player/one",
        "player\u0001one"
    };

    [Fact]
    public void Plugin_message_rejects_null_target_id()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginMessage(null!, "message"));

        Assert.Equal("targetId", ex.ParamName);
    }

    [Fact]
    public void Plugin_message_rejects_null_message()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new PluginMessage("target-1", null!));

        Assert.Equal("message", ex.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidTargetIds))]
    public void Plugin_message_rejects_invalid_target_id(string targetId)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => new PluginMessage(targetId, "message"));

        Assert.Equal("targetId", ex.ParamName);
    }

    [Fact]
    public void Plugin_message_init_rejects_null_inputs()
    {
        var targetEx = Assert.Throws<ArgumentNullException>(
            () => new PluginMessage("target-1", "message") { TargetId = null! });

        Assert.Equal("value", targetEx.ParamName);

        var messageEx = Assert.Throws<ArgumentNullException>(
            () => new PluginMessage("target-1", "message") { Message = null! });

        Assert.Equal("value", messageEx.ParamName);
    }

    [Fact]
    public void In_memory_sink_send_rejects_null_inputs_without_appending()
    {
        var nullTargetSink = new InMemoryPluginMessageSink();

        var targetEx = Assert.Throws<ArgumentNullException>(
            () => nullTargetSink.Send(null!, "message"));

        Assert.Equal("targetId", targetEx.ParamName);
        Assert.Empty(nullTargetSink.Messages);

        var nullMessageSink = new InMemoryPluginMessageSink();

        var messageEx = Assert.Throws<ArgumentNullException>(
            () => nullMessageSink.Send("target-1", null!));

        Assert.Equal("message", messageEx.ParamName);
        Assert.Empty(nullMessageSink.Messages);
    }

    [Theory]
    [MemberData(nameof(InvalidTargetIds))]
    public void In_memory_sink_send_rejects_invalid_target_id_without_appending(string targetId)
    {
        var sink = new InMemoryPluginMessageSink();

        var ex = Assert.Throws<ArgumentException>(
            () => sink.Send(targetId, "message"));

        Assert.Equal("targetId", ex.ParamName);
        Assert.Empty(sink.Messages);
    }

    [Fact]
    public async Task In_memory_sink_send_async_rejects_null_inputs_without_appending()
    {
        var nullTargetSink = new InMemoryPluginMessageSink();

        var targetEx = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullTargetSink.SendAsync(null!, "message").AsTask());

        Assert.Equal("targetId", targetEx.ParamName);
        Assert.Empty(nullTargetSink.Messages);

        var nullMessageSink = new InMemoryPluginMessageSink();

        var messageEx = await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await nullMessageSink.SendAsync("target-1", null!).AsTask());

        Assert.Equal("message", messageEx.ParamName);
        Assert.Empty(nullMessageSink.Messages);
    }

    [Theory]
    [MemberData(nameof(InvalidTargetIds))]
    public async Task In_memory_sink_send_async_rejects_invalid_target_id_without_appending(string targetId)
    {
        var sink = new InMemoryPluginMessageSink();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            async () => await sink.SendAsync(targetId, "message").AsTask());

        Assert.Equal("targetId", ex.ParamName);
        Assert.Empty(sink.Messages);
    }
}
