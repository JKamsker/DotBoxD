using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientProxySurpriseTests
{
    [Fact]
    public void Generated_client_rejects_service_null_reference_defaults()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public interface IEchoService
            {
                ValueTask<int> EchoAsync(string name = null!);
            }

            [ServerExtension("echo", typeof(IEchoService))]
            public sealed partial class EchoKernel
            {
                public int Echo(string name, HookContext ctx)
                {
                    return 1;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("cannot default to null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Direct_generated_client_supports_parameters_that_collide_with_generated_locals()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            [ServerExtension(typeof(IRemoteControl), "direct-local-collision")]
            public sealed partial class EchoKernel
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(int __arguments, HookContext ctx) => __arguments;
            }
            """);

        Assert.Contains(assembly.GetTypes(), type => type.FullName == "Sample.EchoPluginPackage");
    }

    [Fact]
    public void Direct_generated_client_rejects_generic_kernel_types()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl;

            [ServerExtension(typeof(IRemoteControl), "generic-direct")]
            public sealed partial class GenericKernel<T>
            {
                [ServerExtensionMethod(typeof(IRemoteControl))]
                public int Echo(int value, HookContext ctx) => value;
            }
            """);

        Assert.Contains(
            diagnostics,
            d => d.Id == "DBXK100" &&
                 d.GetMessage().Contains("generic", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0246");
    }
}
