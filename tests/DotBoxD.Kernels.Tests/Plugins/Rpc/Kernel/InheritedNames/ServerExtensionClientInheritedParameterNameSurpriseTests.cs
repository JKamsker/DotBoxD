using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientInheritedParameterNameSurpriseTests
{
    [Fact]
    public void Generated_client_reports_conflicting_inherited_parameter_names()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            using System.Threading;
            using System.Threading.Tasks;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;
            using DotBoxD.Services.Attributes;
            using DotBoxD.Abstractions;

            namespace Sample;

            [DotBoxDService]
            public interface IRemoteControl
            {
            }

            public sealed class RemoteControl : IRemoteControl, IServerExtensionClientAccessor
            {
                public RemoteControl(DotBoxD.Abstractions.IServerExtensionClientRegistry serverExtensions)
                    => ServerExtensions = serverExtensions;

                public DotBoxD.Abstractions.IServerExtensionClientRegistry ServerExtensions { get; }
            }

            public interface ILeftCounterService
            {
                ValueTask<int> CountAsync(int left, CancellationToken cancellationToken = default);
            }

            public interface IRightCounterService
            {
                ValueTask<int> CountAsync(int right, CancellationToken cancellationToken = default);
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

            public static class Probe
            {
                public static ValueTask<int> Count(RemoteControl control)
                    => control.Counter.CountAsync(right: 2);
            }
            """);

        var diagnosticText = string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));

        Assert.True(
            diagnostics.Any(d => d.Id == "DBXK100" &&
                                 d.GetMessage().Contains("parameter", StringComparison.OrdinalIgnoreCase) &&
                                 d.GetMessage().Contains("name", StringComparison.OrdinalIgnoreCase)),
            "Expected a focused DBXK100 diagnostic for conflicting inherited parameter names. Actual diagnostics:" +
            Environment.NewLine +
            diagnosticText);
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS1739");
    }
}
