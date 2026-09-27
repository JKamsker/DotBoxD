using System.Net;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafePinnedHttpResponseDisposalTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Dispose_ReleasesTransportEvenWhenContentThrows(bool contentThrows, bool hasOwner)
    {
        var failure = new InvalidOperationException("Content disposal failed.");
        var content = new TrackingContent(contentThrows ? failure : null);
        var message = new HttpResponseMessage { Content = content };
        var owner = hasOwner ? new TrackingOwner() : null;
        var response = new SafePinnedHttpResponse(message, owner);

        if (contentThrows)
        {
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(response.Dispose));
        }
        else
        {
            response.Dispose();
        }

        Assert.Equal(1, content.DisposeCalls);
        if (owner is not null)
        {
            Assert.Equal(1, owner.DisposeCalls);
        }
    }

    private sealed class TrackingOwner : IDisposable
    {
        public int DisposeCalls { get; private set; }
        public void Dispose() => DisposeCalls++;
    }

    private sealed class TrackingContent(Exception? failure) : HttpContent
    {
        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                DisposeCalls++;
                if (failure is not null)
                {
                    throw failure;
                }
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return true;
        }
    }
}
