using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Policies;

namespace DotBoxD.Kernels.Tests.Policy;

[Collection(AllocationMeasurementCollection.Name)]
[Trait(AllocationMeasurementCollection.TraitName, AllocationMeasurementCollection.TraitValue)]
public sealed class ParameterReaderFormattableLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Typed_formatters_do_not_retain_collectible_value_types(bool buildPolicy, bool nullable)
    {
        var (reference, policy) = CreateAndReleaseFormatter(buildPolicy, nullable);
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

        Assert.False(reference.IsAlive, "Compiled formatters must release unused collectible value types.");
        if (buildPolicy)
        {
            Assert.Equal("formatted", Assert.Single(policy!.Grants).Parameters["Value"]);
        }

        GC.KeepAlive(policy);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Reference, SandboxPolicy? Policy) CreateAndReleaseFormatter(bool buildPolicy, bool nullable)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"PolicyFormatter-{Guid.NewGuid():N}"), AssemblyBuilderAccess.RunAndCollect);
        var builder = assembly.DefineDynamicModule("Formatters").DefineType(
            "Formatter", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout,
            typeof(ValueType), [typeof(IFormattable)]);
        var method = builder.DefineMethod(
            nameof(IFormattable.ToString),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot,
            typeof(string), [typeof(string), typeof(IFormatProvider)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "formatted");
        il.Emit(OpCodes.Ret);
        builder.DefineMethodOverride(method, typeof(IFormattable).GetMethod(nameof(IFormattable.ToString))!);
        var valueType = builder.CreateType()!;
        if (!buildPolicy)
        {
            return (new WeakReference(valueType), null);
        }

        var propertyType = nullable ? typeof(Nullable<>).MakeGenericType(valueType) : valueType;
        var parameterType = typeof(PolicyParameterValue<>).MakeGenericType(propertyType);
        var parameters = Activator.CreateInstance(parameterType, [Activator.CreateInstance(valueType)])!;
        var policy = SandboxPolicyBuilder.Create().Grant("custom.parameters", parameters).Build();
        return (new WeakReference(valueType), policy);
    }
}
