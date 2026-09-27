using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Chunks;

public sealed class RpcStreamChunkDisposalTests
{
    [Theory]
    [InlineData("credit")]
    [InlineData("without")]
    [InlineData("credit-then-without")]
    [InlineData("without-then-credit")]
    [InlineData("parallel-credit")]
    [InlineData("parallel-mixed")]
    public void Disposal_returns_the_frame_and_only_the_winning_disposer_can_return_credit(string mode)
    {
        using var fixture = ChunkDisposalFixture.Create();
        var chunk = fixture.Chunk;
        switch (mode)
        {
            case "credit":
                chunk.Dispose();
                chunk.Dispose();
                break;
            case "without":
                chunk.DisposeWithoutCredit();
                chunk.DisposeWithoutCredit();
                break;
            case "credit-then-without":
                chunk.Dispose();
                chunk.DisposeWithoutCredit();
                break;
            case "without-then-credit":
                chunk.DisposeWithoutCredit();
                chunk.Dispose();
                break;
            default:
                Parallel.For(0, 64, index =>
                {
                    if (mode == "parallel-credit" || index % 2 == 0)
                    {
                        chunk.Dispose();
                    }
                    else
                    {
                        chunk.DisposeWithoutCredit();
                    }
                });
                break;
        }

        if (mode == "parallel-mixed")
        {
            Assert.InRange(fixture.Sender.Credits, 0, 1);
        }
        else
        {
            Assert.Equal(mode.StartsWith("without", StringComparison.Ordinal) ? 0 : 1, fixture.Sender.Credits);
        }
        Assert.False(fixture.Sender.InvalidFrame);
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Frame.Memory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Terminal_receivers_do_not_send_more_credit(bool cancel)
    {
        using var fixture = ChunkDisposalFixture.Create();
        if (cancel)
        {
            fixture.Receiver.Cancel();
        }
        else
        {
            fixture.Manager.CompleteInbound(fixture.Receiver.Handle.StreamId);
        }
        fixture.Chunk.Dispose();
        Assert.Equal(0, fixture.Sender.Credits);
        Assert.False(fixture.Sender.InvalidFrame);
        Assert.Throws<ObjectDisposedException>(() => _ = fixture.Frame.Memory);
    }
}
