using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class TaskLikeSymbolRegressionTests
{
    [Fact]
    public void Registration_accumulator_rejects_source_defined_value_task_payload()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            namespace System.Threading.Tasks
            {
                public sealed class ValueTask<T>
                {
                }
            }

            namespace Sample
            {
                using DotBoxD.Abstractions;

                [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
                internal sealed class RemoteServiceControl
                {
                    public System.Threading.Tasks.ValueTask<string> Replace<TService, TKernel>()
                        where TService : class
                        where TKernel : class, TService
                        => new();
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("Task<T> or ValueTask<T>", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }

    [Fact]
    public void Server_extension_client_rejects_source_defined_task_payload_contract()
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics("""
            namespace System.Threading.Tasks
            {
                public sealed class Task<T>
                {
                }
            }

            namespace Sample
            {
                using DotBoxD.Abstractions;
                using DotBoxD.Plugins;

                public interface ICounter
                {
                    System.Threading.Tasks.Task<int> RunAsync(int value);
                }

                [ServerExtension("fake-task-client", typeof(ICounter))]
                public sealed partial class CounterKernel
                {
                    public int Run(int value, HookContext ctx) => value;
                }
            }
            """);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains("return type must match", StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Id.StartsWith("CS", StringComparison.Ordinal));
    }
}
