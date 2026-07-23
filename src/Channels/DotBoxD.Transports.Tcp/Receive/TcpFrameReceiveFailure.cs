using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Creates faulted TCP receive values with stable cancellation tokens.</summary>
internal static class TcpFrameReceiveFailure
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
