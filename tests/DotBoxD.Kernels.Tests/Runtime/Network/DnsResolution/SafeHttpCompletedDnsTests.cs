using System.Net;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.Network;

public sealed class SafeHttpCompletedDnsTests
{
    [Theory]
    [InlineData("Inline", "success")]
    [InlineData("Task", "success")]
    [InlineData("Source", "success")]
    [InlineData("Inline", "fault")]
    [InlineData("Task", "fault")]
    [InlineData("Source", "fault")]
    [InlineData("Inline", "cancel")]
    [InlineData("Task", "cancel")]
    [InlineData("Source", "cancel")]
    public async Task Completed_resolver_preserves_results_failures_and_cancellation(string kind, string outcome)
    {
        using var fixture = new CompletedDnsFixture();
        var scenario = fixture.CreateScenario();
        using var context = scenario.Context;
        using var cancellation = new CancellationTokenSource();
        var source = new CompletedDnsFixture.Source();
        var error = outcome == "fault" ? new IOException("Resolver failed.") : null;
        source.Complete(error);
        var calls = 0;

        var request = fixture.Start(context, Resolve, cancellation.Token);
        if (outcome == "success")
        {
            Assert.Equal("ok", await request);
            Assert.True(Assert.Single(scenario.Audit.Events).Success);
            Assert.True(context.Budget.NetworkBytesRead > 0);
        }
        else
        {
            var failure = await Assert.ThrowsAsync<SandboxRuntimeException>(() => request.AsTask());
            var expected = outcome == "cancel" ? SandboxErrorCode.Cancelled : SandboxErrorCode.HostFailure;
            Assert.Equal(expected, failure.Error.Code);
            Assert.Equal(expected, Assert.Single(scenario.Audit.Events).ErrorCode);
            Assert.Equal(0, context.Budget.NetworkBytesRead);
        }
        Assert.Equal(1, calls);
        Assert.Equal(kind == "Source" ? 1 : 0, source.ResultCalls);

        ValueTask<IReadOnlyList<IPAddress>> Resolve(string host, CancellationToken ct)
        {
            Assert.Equal("api.example.com", host);
            Interlocked.Increment(ref calls);
            if (outcome == "cancel")
            {
                cancellation.Cancel();
            }
            return kind switch
            {
                "Source" => source.Result,
                "Task" => new ValueTask<IReadOnlyList<IPAddress>>(error is null
                    ? Task.FromResult(CompletedDnsFixture.Addresses)
                    : Task.FromException<IReadOnlyList<IPAddress>>(error)),
                _ => error is null
                    ? new ValueTask<IReadOnlyList<IPAddress>>(CompletedDnsFixture.Addresses)
                    : ValueTask.FromException<IReadOnlyList<IPAddress>>(error),
            };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_source_is_consumed_once_and_caller_cancellation_remains_prompt(bool cancel)
    {
        using var fixture = new CompletedDnsFixture();
        var scenario = fixture.CreateScenario();
        using var context = scenario.Context;
        using var cancellation = new CancellationTokenSource();
        var source = new CompletedDnsFixture.Source();
        var request = fixture.Start(context, (_, _) => source.Result, cancellation.Token).AsTask();
        var released = false;
        try
        {
            Assert.False(request.IsCompleted);
            if (cancel)
            {
                cancellation.Cancel();
                var failure = await Assert.ThrowsAsync<SandboxRuntimeException>(() => request.WaitAsync(TimeSpan.FromSeconds(1)));
                Assert.Equal(SandboxErrorCode.Cancelled, failure.Error.Code);
                Assert.Equal(0, source.ResultCalls);
                Assert.Equal(0, context.Budget.NetworkBytesRead);
            }
            source.Complete();
            released = true;
            await source.Consumed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            if (!cancel)
            {
                Assert.Equal("ok", await request.WaitAsync(TimeSpan.FromSeconds(1)));
            }
            Assert.Equal(1, source.ResultCalls);
            Assert.Equal(!cancel, Assert.Single(scenario.Audit.Events).Success);
        }
        finally
        {
            if (!released)
            {
                source.Complete();
            }
            _ = await Record.ExceptionAsync(() => request.WaitAsync(TimeSpan.FromSeconds(1)));
            await source.Consumed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }
}
