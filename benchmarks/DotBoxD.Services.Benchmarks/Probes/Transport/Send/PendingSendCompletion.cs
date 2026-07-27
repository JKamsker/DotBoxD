using System.Diagnostics;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class PendingSendCompletion
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static void Consume(ref ValueTask pending)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!pending.IsCompleted)
        {
            ThrowIfTimedOut(startedAt);
            spinner.SpinOnce();
        }

        pending.GetAwaiter().GetResult();
    }

    public static TResult Consume<TResult>(ref ValueTask<TResult> pending)
    {
        var startedAt = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!pending.IsCompleted)
        {
            ThrowIfTimedOut(startedAt);
            spinner.SpinOnce();
        }

        return pending.GetAwaiter().GetResult();
    }

    private static void ThrowIfTimedOut(long startedAt)
    {
        if (Stopwatch.GetElapsedTime(startedAt) >= Timeout)
        {
            throw new TimeoutException("A controlled pending operation did not complete.");
        }
    }
}
