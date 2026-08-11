using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Transport;

public sealed class StreamConnectionOwnedStreamDisposalFailureRegressionTests
{
    [Fact]
    public async Task CloseAsync_PropagatesOwnedStreamDisposalFailureToAllCloseCallers()
    {
        var expected = new InvalidOperationException("owned stream disposal failed");
        var stream = new ThrowingDisposeStream(expected);
        var connection = new StreamConnection(stream, ownsStream: true);

        var closeException = await Record.ExceptionAsync(() => connection.CloseAsync());
        var disposeException = await Record.ExceptionAsync(async () => await connection.DisposeAsync());

        Assert.Same(expected, closeException);
        Assert.Same(expected, disposeException);
        Assert.True(stream.DisposeAttempted);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => connection.ReceiveAsync());
    }

    private sealed class ThrowingDisposeStream(Exception exception) : MemoryStream
    {
        public bool DisposeAttempted { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            DisposeAttempted = true;
            await base.DisposeAsync();
            throw exception;
        }
    }
}
