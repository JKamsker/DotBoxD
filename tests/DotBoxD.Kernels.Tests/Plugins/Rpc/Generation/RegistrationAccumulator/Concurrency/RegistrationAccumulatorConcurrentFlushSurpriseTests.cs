using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorConcurrentFlushSurpriseTests
{
    [Fact]
    public async Task Concurrent_flushes_do_not_replay_the_same_registration()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                public TaskCompletionSource<bool> RegistrationEntered { get; } = new();

                public TaskCompletionSource<bool> ReleaseRegistration { get; } = new();

                public int Calls { get; private set; }

                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                    => new(ReplaceAsync());

                private async Task<string> ReplaceAsync()
                {
                    Calls++;
                    RegistrationEntered.TrySetResult(true);
                    await ReleaseRegistration.Task;
                    return "registered";
                }
            }

            public interface IService
            {
            }

            public sealed class ServiceKernel : IService
            {
            }

            public static class Probe
            {
                public static async Task<int> RunAsync()
                {
                    var control = new RemoteServiceControl();
                    var accumulator = new ServiceRegistrationAccumulator(control)
                        .Replace<IService, ServiceKernel>();

                    var firstFlush = accumulator.FlushAsync().AsTask();
                    await control.RegistrationEntered.Task;
                    var secondFlush = accumulator.FlushAsync().AsTask();

                    control.ReleaseRegistration.SetResult(true);
                    await Task.WhenAll(firstFlush, secondFlush);

                    return control.Calls;
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<int>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var calls = await task;

        Assert.Equal(1, calls);
    }
}
