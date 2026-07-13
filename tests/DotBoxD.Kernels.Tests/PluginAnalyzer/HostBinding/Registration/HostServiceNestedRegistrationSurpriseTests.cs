using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceNestedRegistrationSurpriseTests
{
    [Fact]
    public void AddBindingsFrom_wraps_throwing_nested_service_getters_with_property_context()
    {
        var builder = new SandboxHostBuilder();
        var world = new ThrowingRootService();

        var ex = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IThrowingRootService>(world));

        Assert.IsNotType<System.Reflection.TargetInvocationException>(ex);
        Assert.Contains("Host service property", ex.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(IThrowingRootService).FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IThrowingRootService.Child), ex.Message, StringComparison.Ordinal);
        Assert.Same(world.GetterFailure, ex.InnerException);
    }
}

[RpcService]
public interface IThrowingRootService
{
    IChildService Child { get; }
}

[RpcService]
public interface IChildService
{
    [HostBinding("probe.read.child", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int Read();
}

internal sealed class ThrowingRootService : IThrowingRootService
{
    public InvalidOperationException GetterFailure { get; } = new("nested getter failed");

    public IChildService Child => throw GetterFailure;
}
