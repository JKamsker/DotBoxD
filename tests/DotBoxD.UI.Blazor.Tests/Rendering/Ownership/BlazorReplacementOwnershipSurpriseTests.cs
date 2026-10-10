namespace DotBoxD.UI.Blazor.Tests.Rendering;

public sealed class BlazorReplacementOwnershipSurpriseTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task Replacement_uses_the_outgoing_attachment_ownership_policy(
        bool outgoingDisposeOnDetach,
        bool incomingDisposeOnDetach,
        bool outgoingDisconnected)
    {
        using var sandbox = UiFixture.Sandbox();
        await using var outgoing = await UiFixture.Host(sandbox).InstallAsync(
            UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var incoming = await UiFixture.Host(sandbox).InstallAsync(
            UiFixture.TextPackage(), new BlazorUiRenderer());
        await using var browser = new TestBlazorRenderer();

        await browser.MountAsync(outgoing, new Authorizer(), outgoingDisposeOnDetach);
        await browser.ReplaceAsync(incoming, new Authorizer(), incomingDisposeOnDetach);

        Assert.Equal(outgoingDisconnected, outgoing.IsDisconnected);
        Assert.False(incoming.IsDisconnected);
        Assert.Empty(browser.Errors);
    }
}
