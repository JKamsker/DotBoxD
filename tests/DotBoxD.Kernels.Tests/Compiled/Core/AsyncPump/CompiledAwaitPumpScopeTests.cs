using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledAwaitPumpScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nested_scope_restores_the_outer_pump_after_success_or_failure(bool fail)
    {
        using var context = CompiledAwaitPumpScopeFixture.Context();
        var outer = new CompiledAwaitPumpScopeFixture.Pump(1);
        var inner = new CompiledAwaitPumpScopeFixture.Pump(2);
        var expected = new InvalidOperationException("ordinary scoped failure");
        CompiledAwaitPumpScopeFixture.AssertNoPump(context);
        using (CompiledBindingDispatcher.InstallAwaitPump(outer))
        {
            Assert.Same(outer.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
            try
            {
                using var scope = CompiledBindingDispatcher.InstallAwaitPump(inner);
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
    }

    [Fact]
    public void Another_thread_owns_an_independent_pump_scope()
    {
        using var context = CompiledAwaitPumpScopeFixture.Context();
        var outer = new CompiledAwaitPumpScopeFixture.Pump(1);
        using var scope = CompiledBindingDispatcher.InstallAwaitPump(outer);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var otherContext = CompiledAwaitPumpScopeFixture.Context();
                CompiledAwaitPumpScopeFixture.AssertNoPump(otherContext);
                var inner = new CompiledAwaitPumpScopeFixture.Pump(2);
                using (CompiledBindingDispatcher.InstallAwaitPump(inner))
                {
                    Assert.Same(inner.Value, CompiledAwaitPumpScopeFixture.Dispatch(otherContext));
                }
                CompiledAwaitPumpScopeFixture.AssertNoPump(otherContext);
            }
            catch (Exception error)
            {
                failure = error;
            }
        })
        { IsBackground = true };
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.Same(outer.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
    }

    [Fact]
    public void Ending_a_scope_preserves_caller_ownership_of_the_pump()
    {
        using var context = CompiledAwaitPumpScopeFixture.Context();
        var pump = new CompiledAwaitPumpScopeFixture.Pump();
        using (CompiledBindingDispatcher.InstallAwaitPump(pump))
        {
            Assert.Same(pump.Value, CompiledAwaitPumpScopeFixture.Dispatch(context));
        }

        Assert.Equal(0, pump.DisposeCalls);
        CompiledAwaitPumpScopeFixture.AssertNoPump(context);
    }

    [Fact]
    public void Selected_pump_receives_the_binding_cancellation_token()
    {
        using var context = CompiledAwaitPumpScopeFixture.Context();
        using var cancellation = new CancellationTokenSource();
        var pump = new CompiledAwaitPumpScopeFixture.Pump();
        using var scope = CompiledBindingDispatcher.InstallAwaitPump(pump);
        Assert.Same(pump.Value, CompiledAwaitPumpScopeFixture.Dispatch(context, cancellation.Token));
        Assert.Equal(cancellation.Token, pump.LastToken);
    }

    [Fact]
    public void Completed_binding_results_keep_their_direct_path()
    {
        using var context = CompiledAwaitPumpScopeFixture.Context();
        var pump = new CompiledAwaitPumpScopeFixture.Pump();
        using var scope = CompiledBindingDispatcher.InstallAwaitPump(pump);
        var result = CompiledBindingDispatcher.AwaitBinding(
            context, ValueTask.FromResult(SandboxValue.Unit), CancellationToken.None);
        Assert.Same(SandboxValue.Unit, result);
        Assert.Equal(0, pump.Calls);
    }
}
