using System.Reflection;
using DotBoxD.Kernels.Runtime;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledAwaitPumpLegacyScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Existing_interface_factory_remains_compatible_with_nested_scopes(bool fail)
    {
        // Existing Hosting binaries include IDisposable in this method's reference signature.
        var factory = typeof(CompiledBindingDispatcher).GetMethod(
                nameof(CompiledBindingDispatcher.InstallAwaitPump), BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ICompiledAwaitPump, IDisposable>>();
        Assert.Equal(typeof(IDisposable), factory.Method.ReturnType);
        using var context = CompiledAwaitPumpScopeFixture.Context();
        var outer = new CompiledAwaitPumpScopeFixture.Pump(1);
        var inner = new CompiledAwaitPumpScopeFixture.Pump(2);
        var expected = new InvalidOperationException("ordinary scoped failure");
        using (factory(outer))
        {
            Assert.Same(outer.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
            try
            {
                using var scope = CompiledBindingDispatcher.EnterAwaitPump(inner);
                Assert.Same(inner.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
                if (fail)
                {
                    throw expected;
                }
            }
            catch (InvalidOperationException error) when (fail && ReferenceEquals(expected, error))
            {
            }
            Assert.Same(outer.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
        }

        CompiledAwaitPumpScopeFixture.AssertNoPump(context);
        Assert.Equal(2, outer.Calls);
        Assert.Equal(1, inner.Calls);
        Assert.Equal(0, outer.DisposeCalls);
        Assert.Equal(0, inner.DisposeCalls);
    }
}
