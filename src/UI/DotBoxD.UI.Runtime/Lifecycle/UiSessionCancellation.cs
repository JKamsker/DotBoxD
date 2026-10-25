namespace DotBoxD.UI.Runtime;

internal static class UiSessionCancellation
{
    [ThreadStatic]
    private static CancellationTokenSource? _cancelling;

    public static bool IsCancelling(CancellationTokenSource source) => ReferenceEquals(_cancelling, source);

    public static Task CancelAsync(CancellationTokenSource source) => Task.Run(() =>
    {
        var previous = _cancelling;
        _cancelling = source;
        try
        { source.Cancel(); }
        finally { _cancelling = previous; }
    });
}
