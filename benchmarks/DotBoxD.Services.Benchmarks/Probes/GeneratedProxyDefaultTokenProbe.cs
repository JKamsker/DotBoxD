using System.Diagnostics;
using Shared;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class GeneratedProxyDefaultTokenProbe
{
    private const int WarmupIterations = 10_000;
    private const int Iterations = 1_000_000;
    private const string ServiceName = "IValueTaskGameService";
    private const string MethodName = "MovePlayerAsync";

    private static readonly MoveRequest s_request = new()
    {
        PlayerId = "player-1",
        X = 1,
        Y = 2,
        Z = 3,
    };

    private static readonly ActionResult s_response = new()
    {
        Success = true,
        Message = "moved",
    };

    public static void Run()
    {
        var invoker = new PendingActionResultInvoker(ServiceName, MethodName, s_request);
        var proxy = new ValueTaskGameServiceProxy(invoker);

        var direct = Measure(
            "Direct pending IRpcInvoker",
            () => invoker.InvokeValueAsync<MoveRequest, ActionResult>(
                ServiceName,
                MethodName,
                s_request,
                CancellationToken.None),
            invoker);
        var generatedProxy = Measure(
            "Generated proxy, default token",
            () => proxy.MovePlayerAsync(s_request, CancellationToken.None),
            invoker);

        if (direct.Checksum != generatedProxy.Checksum)
        {
            throw new InvalidOperationException(
                $"Direct and proxy checksums differ: {direct.Checksum:N0} != {generatedProxy.Checksum:N0}.");
        }

        Console.WriteLine("Generated proxy default-token forwarding probe");
        Console.WriteLine($"iterations = {Iterations:N0}; warmup = {WarmupIterations:N0}");
        Write(direct);
        Write(generatedProxy);
        const string deltaName = "Proxy delta";
        Console.WriteLine(
            $"{deltaName,-36} " +
            $"{generatedProxy.NanosecondsPerOperation - direct.NanosecondsPerOperation,8:N1} ns/op " +
            $"{generatedProxy.BytesPerOperation - direct.BytesPerOperation,8:N1} B/op");
        Console.WriteLine(
            $"invariants: {Iterations:N0} pending calls/lane, one source result read/call, " +
            $"default token observed, checksum={direct.Checksum:N0}");
    }

    private static Measurement Measure(
        string name,
        Func<ValueTask<ActionResult>> invoke,
        PendingActionResultInvoker invoker)
    {
        for (var i = 0; i < WarmupIterations; i++)
        {
            _ = InvokeOnce(invoke, invoker);
        }

        var callsBefore = invoker.CallCount;
        var completionsBefore = invoker.CompletionCount;
        var resultReadsBefore = invoker.ResultReadCount;
        var nonDefaultTokensBefore = invoker.NonDefaultTokenCount;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long checksum = 0;
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < Iterations; i++)
        {
            checksum += InvokeOnce(invoke, invoker);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var calls = invoker.CallCount - callsBefore;
        var completions = invoker.CompletionCount - completionsBefore;
        var resultReads = invoker.ResultReadCount - resultReadsBefore;
        var nonDefaultTokens = invoker.NonDefaultTokenCount - nonDefaultTokensBefore;
        ValidateCounts(name, calls, completions, resultReads, nonDefaultTokens);

        return new Measurement(name, elapsed.TotalMilliseconds, allocated, checksum);
    }

    private static int InvokeOnce(
        Func<ValueTask<ActionResult>> invoke,
        PendingActionResultInvoker invoker)
    {
        var pending = invoke();
        if (pending.IsCompleted)
        {
            throw new InvalidOperationException("The reusable source did not remain pending until explicit completion.");
        }

        invoker.Complete(s_response);
        var result = pending.GetAwaiter().GetResult();
        if (!result.Success || !string.Equals(result.Message, s_response.Message, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The generated proxy changed the pending response.");
        }

        return (result.Success ? 1 : 0) + (result.Message?.Length ?? 0);
    }

    private static void ValidateCounts(
        string name,
        long calls,
        long completions,
        long resultReads,
        long nonDefaultTokens)
    {
        if (calls != Iterations || completions != Iterations || resultReads != Iterations)
        {
            throw new InvalidOperationException(
                $"{name} observed calls/completions/result reads " +
                $"{calls:N0}/{completions:N0}/{resultReads:N0}; expected {Iterations:N0} each.");
        }

        if (nonDefaultTokens != 0)
        {
            throw new InvalidOperationException(
                $"{name} forwarded {nonDefaultTokens:N0} non-default tokens on the default-token lane.");
        }
    }

    private static void Write(Measurement measurement)
    {
        Console.WriteLine(
            $"{measurement.Name,-36} {measurement.Milliseconds,8:N1} ms " +
            $"{measurement.NanosecondsPerOperation,8:N1} ns/op " +
            $"{measurement.AllocatedBytes,14:N0} B " +
            $"{measurement.BytesPerOperation,8:N1} B/op");
    }

    private readonly record struct Measurement(
        string Name,
        double Milliseconds,
        long AllocatedBytes,
        long Checksum)
    {
        public double NanosecondsPerOperation => Milliseconds * 1_000_000 / Iterations;

        public double BytesPerOperation => AllocatedBytes / (double)Iterations;
    }
}
