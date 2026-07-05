using DotBoxD.Services.Diagnostics;
using DotBoxD.Services.Server;
using Xunit;

namespace DotBoxD.Services.Tests.Coverage.Core;

public sealed class RpcEventIsolationReplayRegressionTests
{
    private static readonly SemaphoreSlim s_diagnosticsGate = new(1, 1);

    [Fact]
    public void Raise_DoesNotReplaySubscribersThatAlreadyRanBeforeFailure()
    {
        var calls = new List<string>();
        EventHandler<RpcDiagnosticErrorEventArgs>? handler = null;
        handler += (_, _) => calls.Add("first");
        handler += (_, _) => throw new InvalidOperationException("middle failed");
        handler += (_, _) => calls.Add("third");

        var args = new RpcDiagnosticErrorEventArgs("event isolation", new InvalidOperationException("trigger"));

        var exception = Record.Exception(() => RpcEventHandlerInvoker.Raise(handler, this, args));

        Assert.Null(exception);
        Assert.Equal(new[] { "first", "third" }, calls);
    }

    [Fact]
    public async Task RpcDiagnostics_Report_DoesNotReplaySubscribersThatAlreadyRanBeforeFailure()
    {
        await s_diagnosticsGate.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var operation = "diagnostic isolation " + Guid.NewGuid().ToString("N");
            var calls = new List<string>();

            void First(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (args.Operation == operation)
                {
                    calls.Add("first");
                }
            }

            void Middle(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (args.Operation == operation)
                {
                    throw new InvalidOperationException("middle failed");
                }
            }

            void Third(object? sender, RpcDiagnosticErrorEventArgs args)
            {
                if (args.Operation == operation)
                {
                    calls.Add("third");
                }
            }

            RpcDiagnostics.Error += First;
            RpcDiagnostics.Error += Middle;
            RpcDiagnostics.Error += Third;
            try
            {
                var exception = Record.Exception(
                    () => RpcDiagnostics.Report(operation, new InvalidOperationException("trigger")));

                Assert.Null(exception);
                Assert.Equal(new[] { "first", "third" }, calls);
            }
            finally
            {
                RpcDiagnostics.Error -= First;
                RpcDiagnostics.Error -= Middle;
                RpcDiagnostics.Error -= Third;
            }
        }
        finally
        {
            s_diagnosticsGate.Release();
        }
    }
}
