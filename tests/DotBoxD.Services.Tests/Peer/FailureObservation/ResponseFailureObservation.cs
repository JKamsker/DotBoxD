using Xunit;

namespace DotBoxD.Services.Tests.Peer.FailureObservation;

internal static class ResponseFailureObservation
{
    public static async Task AssertObservedAsync(Func<string, WeakReference> exercise)
    {
        var marker = Guid.NewGuid().ToString("N");
        var notifications = 0;
        EventHandler<UnobservedTaskExceptionEventArgs> handler = (_, args) =>
        {
            if (args.Exception.InnerExceptions.Any(error => ContainsMarker(error, marker)))
            {
                Interlocked.Increment(ref notifications);
                args.SetObserved();
            }
        };
        TaskScheduler.UnobservedTaskException += handler;
        try
        {
            var error = exercise(marker);
            var deadline = Environment.TickCount64 + 5_000;
            do
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (!error.IsAlive)
                {
                    break;
                }

                await Task.Delay(10);
            } while (Environment.TickCount64 < deadline);

            Assert.False(error.IsAlive, "The response error must be collectible before checking notifications.");
            Assert.Equal(0, Volatile.Read(ref notifications));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= handler;
        }
    }

    private static bool ContainsMarker(Exception? error, string marker)
    {
        while (error is not null)
        {
            if (string.Equals(error.Message, marker, StringComparison.Ordinal))
            {
                return true;
            }

            error = error.InnerException;
        }

        return false;
    }
}
