using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;
using Xunit.Abstractions;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ServerExtensionValueTaskProxyAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Completed_ValueTask_calls_do_not_box_a_fresh_completion()
    {
        using var server = PluginServer.Create(
            defaultPolicy: PluginAddendumTestPolicies.LongWall(),
            executionMode: ExecutionMode.Compiled);
        var kernel = await server.InstallServerExtensionAsync(ServerExtensionVoidProxyFixture.UnitPackage());
        var service = ServerExtensionProxy.Create<IValueTaskPingService>(kernel);
        _ = Measure(kernel, null, 1_000);
        _ = Measure(kernel, service, 1_000);

        const int iterations = 1_000;
        var direct = Measure(kernel, null, iterations);
        var proxy = Measure(kernel, service, iterations);
        output.WriteLine($"Direct: {direct / iterations} bytes/call; ValueTask proxy: {proxy / iterations} bytes/call.");

        Assert.Equal(ExecutionMode.Compiled, kernel.LastExecution!.ActualMode);
        Assert.True(kernel.LastExecution.Succeeded);
        // DispatchProxy still allocates its empty argument array.
        Assert.InRange(proxy - direct, 0, 24L * iterations);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    private static long Measure(InstalledKernel kernel, IValueTaskPingService? service, int iterations)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            if (service is null)
            {
                var result = kernel.InvokeServerExtensionAsync(Array.Empty<SandboxValue>()).GetAwaiter().GetResult();
                if (!ReferenceEquals(result, SandboxValue.Unit))
                {
                    throw new InvalidOperationException("The direct kernel call must return Unit.");
                }
            }
            else
            {
                service.PingAsync().GetAwaiter().GetResult();
            }
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
