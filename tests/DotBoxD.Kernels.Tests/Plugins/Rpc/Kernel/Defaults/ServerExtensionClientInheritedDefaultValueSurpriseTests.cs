using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc.Kernel.Defaults;

public sealed class ServerExtensionClientInheritedDefaultValueSurpriseTests
{
    [Fact]
    public void Generated_client_reports_conflicting_inherited_default_values()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;

            namespace Sample;

            [RpcService]
            public interface IRemoteControl
            {
            }

            public sealed class RemoteControl : IRemoteControl, DotBoxD.Abstractions.IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface ILeftCounterService
            {
                ValueTask<int> CountAsync(int value = 1, CancellationToken cancellationToken = default);
            }

            public interface IRightCounterService
            {
                ValueTask<int> CountAsync(int value = 2, CancellationToken cancellationToken = default);
            }

            public interface ICounterService : ILeftCounterService, IRightCounterService
            {
            }

            [ServerExtensionClient(typeof(IRemoteControl))]
            [ServerExtension("counter", typeof(ICounterService))]
            public sealed partial class CounterKernel
            {
                public int Count(int value, HookContext ctx) => value;
            }
            """);

        var diagnosticText = string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

        Assert.True(
            diagnostics.Any(
                d => d.Id == "DBXK100" &&
                     d.GetMessage().Contains("inherited", StringComparison.OrdinalIgnoreCase) &&
                     d.GetMessage().Contains("default", StringComparison.OrdinalIgnoreCase)),
            "Expected a focused DBXK100 diagnostic for conflicting inherited optional/default values. " +
            "Actual diagnostics:" +
            Environment.NewLine +
            diagnosticText);
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS0229");
    }
}
