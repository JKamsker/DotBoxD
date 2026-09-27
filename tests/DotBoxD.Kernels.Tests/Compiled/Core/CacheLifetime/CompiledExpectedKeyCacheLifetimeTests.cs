using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class CompiledExpectedKeyCacheLifetimeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Obsolete_compiled_plans_release_their_cached_identity_strings(int executedEntrypoints)
    {
        var references = PrepareExecuteAndRelease(executedEntrypoints);
        var timer = Stopwatch.StartNew();
        while (references.Any(reference => reference.IsAlive) && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (references.Any(reference => reference.IsAlive))
            {
                await Task.Delay(10);
            }
        }

        Assert.False(references[0].IsAlive, "The execution plan should be released after its host is disposed.");
        Assert.False(references[1].IsAlive, "Cached expected keys must not retain an obsolete plan hash.");
        Assert.False(references[2].IsAlive, "Cached expected keys must not retain an obsolete entrypoint name.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] PrepareExecuteAndRelease(int executedEntrypoints)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        var span = new SourceSpan(1, 1);
        var entrypoints = new[] { "first-" + Guid.NewGuid().ToString("N"), "second-" + Guid.NewGuid().ToString("N") };
        var functions = entrypoints.Select((id, index) => new SandboxFunction(
            id, true, [], SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(index + 1), span), span)])).ToArray();
        var module = new SandboxModule(
            "key-lifetime-" + Guid.NewGuid().ToString("N"), SemVersion.One, SemVersion.One, [], functions,
            new Dictionary<string, string>(StringComparer.Ordinal));
        var policy = SandboxPolicyBuilder.Create().WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
        var plan = host.PrepareAsync(module, policy).AsTask().GetAwaiter().GetResult();
        var options = new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false };
        for (var i = 0; i < executedEntrypoints; i++)
        {
            for (var run = 0; run < 2; run++)
            {
                var result = host.ExecuteAsync(plan, entrypoints[i], SandboxValue.Unit, options)
                    .AsTask().GetAwaiter().GetResult();
                Assert.True(result.Succeeded, result.Error?.SafeMessage);
                Assert.Equal(i + 1, Assert.IsType<I32Value>(result.Value).Value);
            }
        }

        return [new WeakReference(plan), new WeakReference(plan.PlanHash), new WeakReference(entrypoints[0])];
    }
}
