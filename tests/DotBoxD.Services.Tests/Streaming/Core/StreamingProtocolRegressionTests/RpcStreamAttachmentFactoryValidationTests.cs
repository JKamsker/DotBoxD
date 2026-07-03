using System.IO.Pipelines;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Core;

public sealed class RpcStreamAttachmentFactoryValidationTests
{
    [Fact]
    public void AttachmentFactoriesRejectNonPositiveStreamIds()
    {
        AssertHandleRejected(
            () => RpcStreamAttachment.FromStream(
                new RpcStreamHandle(0, RpcStreamKind.Binary),
                Stream.Null));
        AssertHandleRejected(
            () => RpcStreamAttachment.FromStream(
                new RpcStreamHandle(-7, RpcStreamKind.Binary),
                Stream.Null));
        AssertHandleRejected(
            () => RpcStreamAttachment.FromPipe(
                new RpcStreamHandle(0, RpcStreamKind.Binary),
                new Pipe()));
        AssertHandleRejected(
            () => RpcStreamAttachment.FromAsyncEnumerable(
                new RpcStreamHandle(-7, RpcStreamKind.Items),
                EmptyItems()));
    }

    [Fact]
    public void AttachmentFactoriesRejectWrongHandleKind()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => RpcStreamAttachment.FromStream(
                new RpcStreamHandle(1, RpcStreamKind.Items),
                Stream.Null));

        Assert.Equal("handle", ex.ParamName);
        Assert.Contains("Binary", ex.Message);
    }

    private static void AssertHandleRejected(Action create)
    {
        var ex = Assert.Throws<ArgumentException>(create);
        Assert.Equal("handle", ex.ParamName);
    }

    private static async IAsyncEnumerable<int> EmptyItems()
    {
        await Task.Yield();
        yield break;
    }
}
