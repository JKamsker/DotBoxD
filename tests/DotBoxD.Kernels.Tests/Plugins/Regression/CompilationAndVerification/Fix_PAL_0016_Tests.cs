using System.Reflection;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.Regression.CompilationAndVerification;

/// <summary>
/// Regression coverage for PAL-0016: compiler IL emission resolved every
/// <see cref="CompiledRuntime"/> helper by enumerating reflection metadata
/// (<c>typeof(CompiledRuntime).GetMethods(...).Single(...)</c>) at each call-emission
/// site, so cold compile/prepare allocated and repeated method-lookup work proportional
/// to emitted expressions, meters, literals, and type nodes.
///
/// The fix caches the resolved <see cref="MethodInfo"/> per helper name so lookup cost
/// is O(runtime helper count) per process instead of O(emitted helper calls). These tests
/// pin the observable behaviour that must be preserved: the same handle is resolved for a
/// given name, repeated resolutions reuse the cached instance, and unknown names still fail
/// closed exactly as the original <c>.Single(...)</c> did.
/// </summary>
public sealed class Fix_PAL_0016_Tests
{
    [Theory]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.ChargeLoopIteration))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.EnterCall))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.ExitCall))]
    [InlineData(nameof(CompiledRuntime.TypeScalar))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.AddI32))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.CallBinding2))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.CallBinding3))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.ChargeValueArray))]
    [InlineData(nameof(Kernels.Runtime.CompiledRuntime.CreateValueArray))]
    [InlineData(nameof(CompiledRuntime.ListOf))]
    public void Runtime_resolves_the_expected_runtime_helper(string name)
    {
        var resolved = IlEmitterPrimitives.Runtime(name);

        var expected = typeof(CompiledRuntime)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name);

        Assert.Equal(expected, resolved);
        Assert.Equal(name, resolved.Name);
        Assert.True(resolved.IsStatic);
        Assert.True(resolved.IsPublic);
        Assert.Equal(typeof(CompiledRuntime), resolved.DeclaringType);
    }

    [Fact]
    public void Runtime_returns_the_same_cached_handle_for_repeated_lookups()
    {
        // The whole point of the fix: emitting the same helper across many call sites must
        // reuse one cached MethodInfo rather than re-enumerating reflection metadata each time.
        var first = IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel));
        var second = IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel));
        var third = IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel));

        Assert.Same(first, second);
        Assert.Same(second, third);
    }

    [Fact]
    public void Runtime_caches_each_helper_independently()
    {
        var fuel = IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel));
        var enter = IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.EnterCall));

        Assert.NotSame(fuel, enter);
        Assert.Same(fuel, IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel)));
        Assert.Same(enter, IlEmitterPrimitives.Runtime(nameof(Kernels.Runtime.CompiledRuntime.EnterCall)));
    }

    [Fact]
    public void Runtime_fails_closed_for_unknown_helper_names()
    {
        // The original implementation used Single(...), which throws when no public static
        // method matches. The cached resolver must keep failing closed instead of silently
        // returning a wrong or null handle that would emit a broken Call instruction.
        Assert.Throws<InvalidOperationException>(
            () => IlEmitterPrimitives.Runtime("ThisHelperDoesNotExistOnCompiledRuntime"));
    }
}
