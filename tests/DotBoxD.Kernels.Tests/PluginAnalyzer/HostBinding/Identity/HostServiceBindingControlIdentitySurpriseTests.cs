using System.Reflection;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceBindingControlIdentitySurpriseTests
{
    [Fact]
    public void AddBindingsFrom_registers_host_binding_declared_on_foreign_lookalike_control_interface()
    {
        var foreignAssembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly(
            """
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels.Sandbox;

            namespace DotBoxD.Abstractions
            {
                public interface IServiceControl
                {
                    [HostBinding("foreign.control.read", "foreign.control.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
                    int Read();
                }
            }

            namespace ForeignControlIdentity
            {
                public interface IForeignControlService : DotBoxD.Abstractions.IServiceControl;

                public sealed class ForeignControlService : IForeignControlService
                {
                    public int Read() => 42;
                }
            }
            """);
        var serviceContract = foreignAssembly.GetType("ForeignControlIdentity.IForeignControlService")!;
        var implementation = Activator.CreateInstance(
            foreignAssembly.GetType("ForeignControlIdentity.ForeignControlService")!)!;

        using var host = SandboxHost.Create(builder => AddBindingsFrom(builder, serviceContract, implementation));

        var bindings = Assert.IsAssignableFrom<IBindingCatalog>(
            typeof(SandboxHost)
                .GetField("_bindings", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(host));

        Assert.True(bindings.Contains("foreign.control.read"));
    }

    private static void AddBindingsFrom(SandboxHostBuilder builder, Type serviceContract, object implementation)
    {
        var addBindingsFrom = typeof(HostServiceBindingExtensions)
            .GetMethod(nameof(HostServiceBindingExtensions.AddBindingsFrom))!
            .MakeGenericMethod(serviceContract);

        _ = addBindingsFrom.Invoke(null, [builder, implementation]);
    }
}
