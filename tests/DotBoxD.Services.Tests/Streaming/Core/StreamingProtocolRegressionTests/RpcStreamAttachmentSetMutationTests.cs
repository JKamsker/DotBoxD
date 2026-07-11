using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamAttachmentSetMutationTests
{
    [Fact]
    public void FromSingle_NullAttachment_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => RpcStreamAttachmentSet.FromSingle(null!));

        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public void EmptySingle_ReportsStableShapeError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _ = RpcStreamAttachmentSet.Empty.Single);

        Assert.Equal("Attachment set does not contain a single stream.", ex.Message);
    }

    [Fact]
    public void EmptyMany_ReportsStableShapeError()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => _ = RpcStreamAttachmentSet.Empty.Many);

        Assert.Equal("Attachment set is not array-backed.", ex.Message);
    }

    [Fact]
    public void SingleSet_GetAtOutOfRange_ThrowsArgumentOutOfRangeException()
    {
        var attachment = RpcStreamAttachment.FromStream(
            new RpcStreamHandle(3, RpcStreamKind.Binary),
            Stream.Null);
        var set = RpcStreamAttachmentSet.FromSingle(attachment);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => set.GetAt(1));

        Assert.Equal("index", ex.ParamName);
    }
}
