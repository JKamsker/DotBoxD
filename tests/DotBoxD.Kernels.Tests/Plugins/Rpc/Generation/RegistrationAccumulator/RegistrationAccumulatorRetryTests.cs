using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorRetryTests
{
    [Fact]
    public async Task Flush_retry_does_not_replay_completed_registrations()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("ServiceRegistrationAccumulator", "Replace")]
            internal sealed class RemoteServiceControl
            {
                public int StableCalls { get; private set; }

                public int FlakyCalls { get; private set; }

                public ValueTask<string> Replace<TService, TKernel>()
                    where TService : class
                    where TKernel : class, TService
                {
                    if (typeof(TKernel) == typeof(StableKernel))
                    {
                        StableCalls++;
                        return ValueTask.FromResult("stable");
                    }

                    FlakyCalls++;
                    if (FlakyCalls == 1)
                    {
                        throw new InvalidOperationException("transient registration failure");
                    }

                    return ValueTask.FromResult("flaky");
                }
            }

            public interface IService
            {
            }

            public sealed class StableKernel : IService
            {
            }

            public sealed class FlakyKernel : IService
            {
            }

            public static class Probe
            {
                public static async Task<int[]> RunAsync()
                {
                    var control = new RemoteServiceControl();
                    var accumulator = new ServiceRegistrationAccumulator(control)
                        .Replace<IService, StableKernel>()
                        .Replace<IService, FlakyKernel>();

                    var failed = false;
                    try
                    {
                        await accumulator.FlushAsync();
                    }
                    catch (InvalidOperationException)
                    {
                        failed = true;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException("unexpected registration exception", ex);
                    }

                    if (!failed)
                    {
                        throw new InvalidOperationException("first flush should fail");
                    }

                    await accumulator.FlushAsync();

                    return [control.StableCalls, control.FlakyCalls];
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<int[]>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var calls = await task;

        Assert.Equal([1, 2], calls);
    }
}
