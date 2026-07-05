using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Messaging;

public sealed class PluginMessageBindingContractTests
{
    [Fact]
    public void CreateSend_rejects_null_sink_at_registration_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => PluginMessageBindings.CreateSend(null!));

        Assert.Equal("sink", ex.ParamName);
    }

    [Fact]
    public void AddPluginMessageBindings_rejects_null_sink_at_registration_boundary()
    {
        var builder = new SandboxHostBuilder();

        var ex = Assert.Throws<ArgumentNullException>(
            () => builder.AddPluginMessageBindings(null!));

        Assert.Equal("sink", ex.ParamName);
    }

    [Fact]
    public void AddPluginMessageBindings_rejects_null_builder_at_registration_boundary()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => PluginMessageBindings.AddPluginMessageBindings(null!, new InMemoryPluginMessageSink()));

        Assert.Equal("builder", ex.ParamName);
    }
}
