using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Policies;
using DotBoxD.Plugins.Runtime;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

/// <summary>
/// <c>Length</c> on an array event property is a list count; it must not be claimed (and rejected) by the
/// string.Length lowerer.
/// </summary>
public sealed class PluginAnalyzerArrayLengthTests
{
    [Fact]
    public async Task Array_length_lowers_to_list_count()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.Create("""
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record IdsEvent(string TargetId, int[] Ids, string Name);

            [Plugin("generated-array-length")]
            public sealed partial class IdsKernel : IEventKernel<IdsEvent>
            {
                public bool ShouldHandle(IdsEvent e, HookContext ctx)
                    => e.Ids.Length > 1 && e.Name.Length > 0;

                public void Handle(IdsEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, "hit");
            }
            """, "Sample.IdsPluginPackage");
        var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddPluginMessageBindings(new InMemoryPluginMessageSink());
            builder.UseInterpreter();
        });
        var policy = SandboxPolicyBuilder.Create()
            .GrantHostMessageWrite()
            .WithFuel(100_000)
            .WithWallTime(TimeSpan.FromSeconds(30))
            .WithMaxHostCalls(1_000)
            .Build();
        var plan = await host.PrepareAsync(package.Module, policy);

        Assert.True(await ShouldHandleAsync(host, plan, package, [1, 2], "a"));
        Assert.False(await ShouldHandleAsync(host, plan, package, [1], "a"));
        Assert.False(await ShouldHandleAsync(host, plan, package, [1, 2], ""));
    }

    private static async Task<bool> ShouldHandleAsync(
        SandboxHost host,
        ExecutionPlan plan,
        PluginPackage package,
        int[] ids,
        string name)
    {
        var input = SandboxValue.FromList([
            SandboxValue.FromString("player-1"),
            SandboxValue.FromList(ids.Select(id => SandboxValue.FromInt32(id)).ToArray(), SandboxType.I32),
            SandboxValue.FromString(name)
        ]);
        var result = await host.ExecuteAsync(
            plan,
            package.Entrypoints.ShouldHandle,
            input,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        return ((BoolValue)result.Value!).Value;
    }
}
