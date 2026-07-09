using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

/// <summary>
/// Behavioral coverage for the public diagnostics hook, error/exception types, attributes, and
/// event-args records. <see cref="RpcDiagnostics.Report"/> is internal, but it is reachable from a
/// real public path: <see cref="InstanceRegistry"/> reports a faulting sub-service dispose through
/// it during teardown. <see cref="RpcDiagnostics.Error"/> is a process-wide static event, so the
/// trigger tests serialize on a shared gate and filter to their own marker operation to stay
/// deterministic even when the rest of the suite runs in parallel.
/// </summary>
public sealed class DiagnosticsErrorReportingCoverageTests
{
    // RpcDiagnostics.Error is a static event shared by the whole process. Serialize the
    // subscribe/trigger/unsubscribe tests so handlers from one test never observe another's report.
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public async Task RpcDiagnostics_Error_RaisedWithOperationAndError_OnFaultingInstanceDispose()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var observed = new TaskCompletionSource<RpcDiagnosticErrorEventArgs>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var boom = new InvalidOperationException("dispose blew up");

            void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                // Only react to the fault our own throwing disposable produced.
                if (ReferenceEquals(args.Error, boom))
                {
                    observed.TrySetResult(args);
                }
            }

            RpcDiagnostics.Error += Handler;
            try
            {
                var registry = new InstanceRegistry();
                registry.Register("svc", new ThrowingDisposable(boom));

                // ReleaseAll disposes the registered instance; the dispose throws and the registry
                // must route that fault to diagnostics instead of breaking teardown.
                registry.ReleaseAll();

                var args = await observed.Task.WaitAsync(TimeSpan.FromSeconds(30));

                Assert.Same(boom, args.Error);
                Assert.False(string.IsNullOrEmpty(args.Operation));
                // RpcDiagnostics raises Error with a null sender on both the normal and retry paths.
            }
            finally
            {
                RpcDiagnostics.Error -= Handler;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public async Task RpcDiagnostics_Error_AfterUnsubscribe_HandlerNoLongerInvoked()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var invocations = 0;
            var boom = new InvalidOperationException("second fault");

            void Handler(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    Interlocked.Increment(ref invocations);
                }
            }

            RpcDiagnostics.Error += Handler;
            RpcDiagnostics.Error -= Handler;

            var registry = new InstanceRegistry();
            registry.Register("svc", new ThrowingDisposable(boom));
            registry.ReleaseAll();

            // Give any erroneous async dispatch a chance, then assert the unsubscribed handler stayed
            // silent. Disposal reporting is synchronous within ReleaseAll, so the count is final here.
            Assert.Equal(0, Volatile.Read(ref invocations));
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public async Task RpcDiagnostics_Error_FaultingSubscriber_IsIsolatedFromOtherSubscribers()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var boom = new InvalidOperationException("isolation fault");
            var secondObserved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Faulting(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    throw new Exception("handler is hostile");
                }
            }

            void Good(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (ReferenceEquals(args.Error, boom))
                {
                    secondObserved.TrySetResult(true);
                }
            }

            RpcDiagnostics.Error += Faulting;
            RpcDiagnostics.Error += Good;
            try
            {
                var registry = new InstanceRegistry();
                registry.Register("svc", new ThrowingDisposable(boom));

                // A throwing subscriber must not stop the next subscriber from seeing the event, and
                // must not bubble out of teardown.
                registry.ReleaseAll();

                Assert.True(await secondObserved.Task.WaitAsync(TimeSpan.FromSeconds(30)));
            }
            finally
            {
                RpcDiagnostics.Error -= Faulting;
                RpcDiagnostics.Error -= Good;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }

    [Fact]
    public void RpcDiagnosticErrorEventArgs_ExposesConstructorValues()
    {
        var error = new InvalidOperationException("kaboom");

        var args = new RpcDiagnosticErrorEventArgs("teardown", error);

        Assert.Equal("teardown", args.Operation);
        Assert.Same(error, args.Error);
        Assert.IsAssignableFrom<EventArgs>(args);
    }

    [Fact]
    public void RpcDiagnosticErrorEventArgs_RejectsNullConstructorArguments()
    {
        DiagnosticAssert.ArgumentNull(
            () => new RpcDiagnosticErrorEventArgs(null!, new InvalidOperationException("kaboom")),
            "operation");
        DiagnosticAssert.ArgumentNull(() => new RpcDiagnosticErrorEventArgs("teardown", null!), "error");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void RpcDiagnosticErrorEventArgs_RejectsBlankOperation(string operation)
    {
        DiagnosticAssert.Argument(
            () => new RpcDiagnosticErrorEventArgs(operation, new InvalidOperationException("kaboom")),
            "operation");
    }

    // --- InstanceRegistry observable behavior gaps ---

    [Fact]
    public void InstanceRegistry_TryGet_RegisteredInstance_ReturnsTrueAndInstance()
    {
        var registry = new InstanceRegistry();
        var instance = new object();
        var id = registry.Register("svc", instance);

        var found = registry.TryGet("svc", id, out var resolved);

        Assert.True(found);
        Assert.Same(instance, resolved);
    }

    [Fact]
    public void InstanceRegistry_TryGet_UnknownInstance_ReturnsFalseAndNull()
    {
        var registry = new InstanceRegistry();

        var found = registry.TryGet("svc", "does-not-exist", out var resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void InstanceRegistry_TryGet_AfterRelease_ReturnsFalse()
    {
        var registry = new InstanceRegistry();
        var id = registry.Register("svc", new object());

        registry.Release("svc", id);

        Assert.False(registry.TryGet("svc", id, out _));
    }

    [Fact]
    public void InstanceRegistry_Register_NullInstance_ThrowsArgumentNull()
    {
        var registry = new InstanceRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("svc", null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void InstanceRegistry_Constructor_NonPositiveMax_Throws(int maxInstances)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InstanceRegistry(maxInstances));
    }

    // --- IServiceDispatcher interface default member ---

}

internal static class DiagnosticAssert
{
    public static void Argument(Action action, string paramName)
    {
        var ex = Assert.Throws<ArgumentException>(action);

        Assert.Equal(paramName, ex.ParamName);
    }

    public static void ArgumentNull(Action action, string paramName)
    {
        var ex = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(paramName, ex.ParamName);
    }
}
