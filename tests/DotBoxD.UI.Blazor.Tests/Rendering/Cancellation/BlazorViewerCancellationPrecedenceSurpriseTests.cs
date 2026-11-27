namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorViewerCancellationPrecedenceSurpriseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Viewer_fault_after_caller_cancellation_surfaces_the_caller_token(bool faultedTask)
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var callerCancellation = new CancellationTokenSource();
        using var subscription = renderer.Subscribe(CancellingFault(callerCancellation, faultedTask));

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.UpdateAsync(
            [new UiPropertyValue(1, UiPropertyId.Text, UiValue.FromString("changed"))], callerCancellation.Token).AsTask());

        Assert.Equal(callerCancellation.Token, error.CancellationToken);
        Assert.Equal(1, renderer.Updates);
    }

    [Fact]
    public async Task Viewer_fault_with_a_live_caller_token_remains_an_ordinary_fault()
    {
        using var sandbox = UiFixture.Sandbox();
        var renderer = new BlazorUiRenderer();
        await using var session = await UiFixture.Host(sandbox).InstallAsync(UiFixture.TextPackage(), renderer);
        using var callerCancellation = new CancellationTokenSource();
        using var subscription = renderer.Subscribe(() => Task.FromException(new InvalidOperationException("viewer callback failed")));

        await Assert.ThrowsAsync<InvalidOperationException>(() => renderer.UpdateAsync(
            [new UiPropertyValue(1, UiPropertyId.Text, UiValue.FromString("changed"))], callerCancellation.Token).AsTask());
    }

    private static Func<Task> CancellingFault(CancellationTokenSource callerCancellation, bool faultedTask)
    {
        if (faultedTask)
        {
            return () =>
            {
                callerCancellation.Cancel();
                return Task.FromException(new InvalidOperationException("viewer callback failed"));
            };
        }

        return () =>
        {
            callerCancellation.Cancel();
            throw new InvalidOperationException("viewer callback failed");
        };
    }
}
