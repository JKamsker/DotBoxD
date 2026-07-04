namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeInMemoryHttpMessageInvokerCancellationTests
{
    [Fact]
    public async Task SendAsync_with_pre_canceled_token_throws_before_returning_response()
    {
        using var invoker = new SafeInMemoryHttpMessageInvoker("ok");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/config");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(request, cancellation.Token));
    }
}
