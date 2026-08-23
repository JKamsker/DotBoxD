using DotBoxD.Kernels.Sandbox;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceRegistrationAtomicitySurpriseTests
{
    [Fact]
    public void AddBindingsFrom_leaves_builder_reusable_after_nested_service_getter_fails()
    {
        var builder = new SandboxHostBuilder();
        var service = new ThrowingAtomicRegistrationRootService();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddBindingsFrom<IAtomicRegistrationRootService>(service));

        Assert.Contains("Host service property", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IAtomicRegistrationRootService.Child), exception.Message, StringComparison.Ordinal);
        Assert.Same(service.GetterFailure, exception.InnerException);

        builder.AddBindingsFrom<IAtomicRegistrationBaseService>(service);

        using var host = builder.Build();
    }
}

[RpcService]
public interface IAtomicRegistrationBaseService
{
    [HostBinding("probe.read.atomicregistration", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int Read();
}

[RpcService]
public interface IAtomicRegistrationRootService : IAtomicRegistrationBaseService
{
    IAtomicRegistrationChildService Child { get; }
}

[RpcService]
public interface IAtomicRegistrationChildService
{
    [HostBinding("probe.read.atomicregistration.child", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
    int Read();
}

internal sealed class ThrowingAtomicRegistrationRootService : IAtomicRegistrationRootService
{
    public InvalidOperationException GetterFailure { get; } = new("nested getter failed");

    public IAtomicRegistrationChildService Child => throw GetterFailure;

    public int Read() => 42;
}
