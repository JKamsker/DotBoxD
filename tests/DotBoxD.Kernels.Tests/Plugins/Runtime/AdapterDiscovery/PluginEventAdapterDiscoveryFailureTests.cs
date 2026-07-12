using System.Reflection;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Runtime;

public sealed class PluginEventAdapterDiscoveryFailureTests
{
    private static readonly InvalidOperationException AdapterFailure = new("Adapter Instance failed.");

    [Fact]
    public void Resolve_wraps_throwing_static_instance_getter_with_adapter_context()
    {
        var registry = new PluginEventAdapterRegistry();

        var failure = Record.Exception(() => registry.Resolve<ThrowingInstanceEvent>());

        Assert.NotNull(failure);
        Assert.IsNotType<TargetInvocationException>(failure);
        var ex = Assert.IsType<InvalidOperationException>(failure);
        Assert.Contains(typeof(ThrowingInstanceAdapter).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Same(AdapterFailure, ex.InnerException);
    }

    private sealed record ThrowingInstanceEvent(string Value);

    private sealed class ThrowingInstanceAdapter : IPluginEventAdapter<ThrowingInstanceEvent>
    {
        public static ThrowingInstanceAdapter Instance => throw AdapterFailure;

        public string EventName => nameof(ThrowingInstanceEvent);

        public IReadOnlyList<Parameter> Parameters { get; } = [new("e_Value", SandboxType.String)];

        public IReadOnlyList<SandboxValue> ToSandboxValues(ThrowingInstanceEvent e)
            => [SandboxValue.FromString(e.Value)];
    }
}
