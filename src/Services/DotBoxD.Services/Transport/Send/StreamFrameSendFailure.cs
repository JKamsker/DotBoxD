using System.Runtime.ExceptionServices;

namespace DotBoxD.Services.Transport;

/// <summary>Creates completed owned-frame send failures with their original exception shape.</summary>
internal static class StreamFrameSendFailure
{
    public static ValueTask Create(Exception error)
    {
        if (error is OperationCanceledException canceled)
        {
            return CreateCanceledAsync(canceled);
        }

        return new ValueTask(Task.FromException(error));
    }

    /// <summary>
    /// Close ends a started write or flush through the connection-lifetime token. The caller did not
    /// cancel, so the send lost its connection: report it as the same exception a send after close gets
    /// (as a socket transport does), never as a cancellation that callers and retry policies treat as
    /// deliberate.
    /// </summary>
    public static bool IsCloseCancelledWrite(
        StreamConnection connection,
        Exception error,
        CancellationToken callerToken) =>
        error is OperationCanceledException &&
        connection.WriteLifetimeToken.IsCancellationRequested &&
        !callerToken.IsCancellationRequested;

    public static Exception TranslateStartedWrite(
        StreamConnection connection,
        Exception error,
        CancellationToken callerToken) =>
        IsCloseCancelledWrite(connection, error, callerToken)
            ? CreateClosed()
            : error;

    public static ObjectDisposedException CreateClosed() => new(nameof(StreamConnection));

    private static async ValueTask CreateCanceledAsync(OperationCanceledException error)
    {
        await default(ValueTask).ConfigureAwait(false);
        ExceptionDispatchInfo.Capture(error).Throw();
    }
}
