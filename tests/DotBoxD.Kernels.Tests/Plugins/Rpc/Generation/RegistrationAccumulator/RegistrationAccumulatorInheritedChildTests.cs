using System.Reflection;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RegistrationAccumulatorInheritedChildTests
{
    [Fact]
    public async Task Generated_root_accumulator_includes_inherited_public_child_controls()
    {
        var assembly = PluginAnalyzerGeneratedPackageFactory.CreateAssembly("""
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using DotBoxD.Abstractions;

            namespace Sample;

            [GeneratePluginRegistrationAccumulator("RemoteMonsterExtensionAccumulator", "Extend")]
            internal sealed class RemoteMonsterControl
            {
                public List<string> Calls { get; } = [];

                public ValueTask<string> Extend<TService, TKernel>()
                    where TService : class
                    where TKernel : class
                {
                    Calls.Add("extend:" + typeof(TService).Name + ":" + typeof(TKernel).Name);
                    return ValueTask.FromResult("extension");
                }
            }

            internal class RemoteWorldControlBase
            {
                public RemoteMonsterControl Monsters { get; } = new();
            }

            [GeneratePluginRegistrationRootAccumulator("WorldRegistrationAccumulator")]
            internal sealed class RemoteWorldControl : RemoteWorldControlBase
            {
            }

            public interface IMonsterService
            {
            }

            public sealed class MonsterExtensionKernel
            {
            }

            public static class Probe
            {
                public static async Task<string> RunAsync()
                {
                    var world = new RemoteWorldControl();
                    var accumulator = new WorldRegistrationAccumulator(world);
                    accumulator.Monsters.Extend<IMonsterService, MonsterExtensionKernel>();
                    await accumulator.FlushAsync();
                    return world.Monsters.Calls[0];
                }
            }
            """);
        var probe = assembly.GetType("Sample.Probe", throwOnError: true)!;

        var task = (Task<string>)probe.GetMethod("RunAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        var call = await task;

        Assert.Equal("extend:IMonsterService:MonsterExtensionKernel", call);
    }
}
