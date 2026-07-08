using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackPolymorphicDtoUnionTests
{
    [Fact]
    public void Serializer_round_trips_explicit_union_list_member()
    {
        var serializer = new MessagePackRpcSerializer();
        var original = new UnionLayoutSpec
        {
            Id = "main",
            Widgets =
            [
                new UnionTextWidgetSpec { Id = "title", Text = "Deploy" },
                new UnionPanelWidgetSpec
                {
                    Id = "panel",
                    Children = [new UnionTextWidgetSpec { Id = "child", Text = "Ready" }],
                },
            ],
        };
        var writer = new ArrayBufferWriter<byte>();

        serializer.Serialize(writer, original);
        var roundTripped = serializer.Deserialize<UnionLayoutSpec>(writer.WrittenMemory);

        Assert.Equal("main", roundTripped.Id);
        var text = Assert.IsType<UnionTextWidgetSpec>(roundTripped.Widgets[0]);
        Assert.Equal("Deploy", text.Text);
        var panel = Assert.IsType<UnionPanelWidgetSpec>(roundTripped.Widgets[1]);
        var child = Assert.IsType<UnionTextWidgetSpec>(Assert.Single(panel.Children));
        Assert.Equal("Ready", child.Text);
    }

    [MessagePackObject]
    public sealed class UnionLayoutSpec
    {
        [Key(0)]
        public string Id { get; set; } = "";

        [Key(1)]
        public IReadOnlyList<UnionWidgetSpec> Widgets { get; set; } = Array.Empty<UnionWidgetSpec>();
    }

    [MessagePackObject]
    [Union(0, typeof(UnionTextWidgetSpec))]
    [Union(1, typeof(UnionPanelWidgetSpec))]
    public abstract class UnionWidgetSpec
    {
        [Key(0)]
        public string Id { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class UnionTextWidgetSpec : UnionWidgetSpec
    {
        [Key(1)]
        public string Text { get; set; } = "";
    }

    [MessagePackObject]
    public sealed class UnionPanelWidgetSpec : UnionWidgetSpec
    {
        [Key(1)]
        public IReadOnlyList<UnionWidgetSpec> Children { get; set; } = Array.Empty<UnionWidgetSpec>();
    }
}
