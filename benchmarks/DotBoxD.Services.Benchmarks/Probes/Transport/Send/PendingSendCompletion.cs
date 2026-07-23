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
            if (Stopwatch.GetElapsedTime(startedAt) >= Timeout)
            {
                throw new TimeoutException("A controlled pending send did not complete.");
            }

            spinner.SpinOnce();
        }

        pending.GetAwaiter().GetResult();
    }
}
