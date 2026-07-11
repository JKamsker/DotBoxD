namespace DotBoxD.Kernels.Tests.Plugins.Hooks;

public sealed class HookContextContractTests
{
    [Fact]
    public void Hook_context_rejects_null_message_sink()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => new HookContext(null!, CancellationToken.None));

        Assert.Equal("messages", ex.ParamName);
    }
}
