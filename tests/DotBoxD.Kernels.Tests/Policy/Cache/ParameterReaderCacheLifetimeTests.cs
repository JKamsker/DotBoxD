using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ParameterReaderCacheLifetimeTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Policy_snapshots_do_not_retain_collectible_parameter_types(bool buildPolicies, bool hasProperty)
    {
        var (reference, policies) = CreateAndReleaseParameterType(buildPolicies, hasProperty);
        var timer = Stopwatch.StartNew();
        while (reference.IsAlive && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (reference.IsAlive)
            {
                await Task.Delay(10);
            }
        }

        Assert.False(reference.IsAlive, "The parameter reader cache must release unused collectible parameter types.");
        if (buildPolicies && hasProperty)
        {
            Assert.Equal("before", Assert.Single(policies[0].Grants).Parameters["Value"]);
            Assert.Equal("after", Assert.Single(policies[1].Grants).Parameters["Value"]);
        }
        else
        {
            Assert.All(policies, policy => Assert.Empty(Assert.Single(policy.Grants).Parameters));
        }

        GC.KeepAlive(policies);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, SandboxPolicy[] Policies) CreateAndReleaseParameterType(
        bool buildPolicies,
        bool hasProperty)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"PolicyParameters-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Parameters")
            .DefineType("Parameters", TypeAttributes.Public, hasProperty ? typeof(ParameterState) : typeof(object));
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var type = builder.CreateType()!;
        var parameters = Activator.CreateInstance(type)!;
        if (!buildPolicies)
        {
            return (new WeakReference(type), []);
        }

        var first = SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build();
        if (parameters is ParameterState state)
        {
            state.Value = "after";
        }

        var second = SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build();
        return (new WeakReference(type), [first, second]);
    }

    public class ParameterState
    {
        public string Value { get; set; } = "before";
    }
}
