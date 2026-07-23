namespace DotBoxD.Services.Transport;

/// <summary>Creates faulted frame receive values with stable cancellation tokens.</summary>
internal static class FrameReceiveFailure
{
    public static ValueTask<RpcFrame> Create(
        Exception error,
        CancellationToken callerToken)
    {
        if (error is not OperationCanceledException canceled)
        {
            return StreamFrameReadOperations.CreateFailedReceive(error);
        }

        var cancellationToken = canceled.CancellationToken;
        if (!cancellationToken.IsCancellationRequested)
        {
            cancellationToken = callerToken.IsCancellationRequested
                ? callerToken
                : new CancellationToken(canceled: true);
        }

        return StreamFrameReadOperations.CreateCanceledReceive(cancellationToken);
    }
}
