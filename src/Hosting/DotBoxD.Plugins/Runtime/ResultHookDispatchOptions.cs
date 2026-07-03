namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Per-dispatch options for result-returning hooks. Local handlers keep the existing in-process semantics; the
/// timeout applies only to remote result handlers because they cross an IPC boundary that can stall.
/// </summary>
public sealed class ResultHookDispatchOptions<TResult>
    where TResult : struct, IHookResult
{
    private static readonly TimeSpan MaxRemoteHandlerTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    public static ResultHookDispatchOptions<TResult> Default { get; } = new();

    public TimeSpan RemoteHandlerTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public TResult? RemoteTimeoutResult { get; init; }

    public static ResultHookDispatchOptions<TResult> FailClosedAfter(TimeSpan timeout, TResult result)
    {
        ValidateRemoteHandlerTimeout(timeout, nameof(timeout), allowInfinite: false);
        ValidateRemoteTimeoutResult(result, nameof(result));

        return new()
        {
            RemoteHandlerTimeout = timeout,
            RemoteTimeoutResult = result,
        };
    }

    internal void Validate()
    {
        ValidateRemoteHandlerTimeout(RemoteHandlerTimeout, nameof(RemoteHandlerTimeout), allowInfinite: true);
        if (RemoteTimeoutResult is { Success: false })
        {
            throw new ArgumentException(
                "Remote timeout result must be successful.",
                nameof(RemoteTimeoutResult));
        }
    }

    private static void ValidateRemoteHandlerTimeout(TimeSpan timeout, string paramName, bool allowInfinite)
    {
        if ((allowInfinite && timeout == Timeout.InfiniteTimeSpan) ||
            (timeout > TimeSpan.Zero && timeout <= MaxRemoteHandlerTimeout))
        {
            return;
        }

        throw new ArgumentOutOfRangeException(
            paramName,
            timeout,
            $"Remote handler timeout must be positive, no greater than {MaxRemoteHandlerTimeout}, or Timeout.InfiniteTimeSpan.");
    }

    private static void ValidateRemoteTimeoutResult(TResult result, string paramName)
    {
        if (!result.Success)
        {
            throw new ArgumentException("Remote timeout result must be successful.", paramName);
        }
    }
}
