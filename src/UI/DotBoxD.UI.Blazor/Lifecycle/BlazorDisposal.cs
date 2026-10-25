using System.Runtime.ExceptionServices;

namespace DotBoxD.UI.Blazor;

internal static class BlazorDisposal
{
    [ThreadStatic]
    private static CancellationTokenSource? _cancelling;

    public static bool IsCancelling(CancellationTokenSource source) => ReferenceEquals(_cancelling, source);

    public static async Task RunAsync(CancellationTokenSource source, Func<Task>? cleanup)
    {
        ExceptionDispatchInfo? failure = null;
        try
        { await Task.Run(() => Cancel(source)).ConfigureAwait(false); }
        catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
        try
        {
            if (cleanup is not null)
            { await cleanup().ConfigureAwait(false); }
        }
        catch when (failure is not null) { }
        finally { source.Dispose(); }
        failure?.Throw();
    }

    private static void Cancel(CancellationTokenSource source)
    {
        var previous = _cancelling;
        _cancelling = source;
        try
        { source.Cancel(); }
        finally { _cancelling = previous; }
    }
}
