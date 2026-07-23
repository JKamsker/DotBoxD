using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Task-backed overflow when every reusable TCP receive source is active.</summary>
internal static class TcpFrameReceiveFallback
{
    public static ValueTask<RpcFrame> StartFromBeginning(
        TcpConnection connection,
        CancellationToken ct) =>
        ContinueFromBeginningAsync(connection, ct);

    public static ValueTask<RpcFrame> Start(
        TcpConnection connection,
        FrameReceiveOperationState state,
        ValueTask<int> pendingRead) =>
        ContinueAsync(connection, state, pendingRead);

    private static async ValueTask<RpcFrame> ContinueFromBeginningAsync(
        TcpConnection connection,
        CancellationToken ct)
    {
        var owner = new StreamFrameReceiveOwner();
        RpcFrame completedFrame = default;
        try
        {
            connection.ThrowIfDisposedForReceive();
            ct.ThrowIfCancellationRequested();
            var readToken = ct;
            var remaining = StreamFrameReadOperations.BeginFrame(
                ref connection.FrameReceiveBuffer);
            while (true)
            {
                readToken = StreamFrameReadOperations.StartTimeout(
                    connection.FrameReceiveTimeout,
                    ct,
                    connection.FrameReadIdleTimeout,
                    remaining);
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        var pendingRead = connection.FrameReceiveStream.ReadAsync(
                            StreamFrameReadOperations.PrepareRead(
                                ref connection.FrameReceiveBuffer,
                                connection.FrameReceiveLengthBuffer,
                                ref owner,
                                remaining),
                            readToken);
                        StreamFrameReadOperations.ObservePendingRead(
                            ref connection.FrameReceiveBuffer,
                            owner,
                            pendingRead.IsCompletedSuccessfully);
                        read = await pendingRead.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (
                        StreamFrameReadOperations.IsTimeoutCancellation(
                            connection.FrameReceiveTimeout))
                    {
                        throw FrameReadTimeoutSource.CreateTimeoutException(
                            connection.FrameReadIdleTimeout);
                    }

                    if (read == 0)
                    {
                        completedFrame = StreamFrameReadOperations.HandleEndOfStream(
                            owner,
                            remaining,
                            StreamFrameReadProgressFormat.Body);
                        goto ReceiveCompleted;
                    }

                    remaining = StreamFrameReadOperations.CommitRead(
                        ref connection.FrameReceiveBuffer,
                        ref owner,
                        remaining,
                        read);
                    readToken = StreamFrameReadOperations.RearmTimeout(
                        connection.FrameReceiveTimeout,
                        readToken,
                        connection.FrameReadIdleTimeout,
                        remaining);
                }

                if (owner.IsAllocated)
                {
                    completedFrame = owner.TransferFrame(
                        connection.FrameReceiveBuffer.WriterBackedOwner);
                    goto ReceiveCompleted;
                }

                remaining = StreamFrameReadOperations.InitializeOwner(
                    ref connection.FrameReceiveBuffer,
                    connection.FrameReceiveLengthBuffer,
                    MessageFramer.MaxMessageSize,
                    connection.FrameReceiveBuffer.WriterBackedOwner,
                    ref owner);
            }
        }
        catch (Exception error)
        {
            FailScalarReceive(connection, ref owner, error);
            throw;
        }

    ReceiveCompleted:
        return CompleteScalarReceive(connection, ref owner, completedFrame);
    }

    private static RpcFrame CompleteScalarReceive(
        TcpConnection connection,
        ref StreamFrameReceiveOwner owner,
        RpcFrame frame)
    {
        try
        {
            connection.FinishFrameReceive(
                ref owner,
                connection.FrameReceiveBuffer.WriterBackedOwner);
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        return frame;
    }

    [DoesNotReturn]
    private static void FailScalarReceive(
        TcpConnection connection,
        ref StreamFrameReceiveOwner owner,
        Exception error)
    {
        try
        {
            connection.FinishFrameReceive(
                ref owner,
                connection.FrameReceiveBuffer.WriterBackedOwner);
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static async ValueTask<RpcFrame> ContinueAsync(
        TcpConnection connection,
        FrameReceiveOperationState state,
        ValueTask<int> pendingRead)
    {
        while (true)
        {
            var read = 0;
            try
            {
                read = await pendingRead.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                StreamFrameReadOperations.IsTimeoutCancellation(connection.FrameReceiveTimeout))
            {
                FailTimeout(connection, ref state);
            }
            catch (Exception error)
            {
                FailReceive(connection, ref state, error);
            }

            var completed = false;
            RpcFrame frame = default;
            try
            {
                completed = TcpFrameReceiveDriver.ResumeAfterRead(
                    connection,
                    ref state,
                    read,
                    out frame,
                    out pendingRead);
            }
            catch (Exception error)
            {
                FailReceive(connection, ref state, error);
            }

            if (completed)
            {
                return CompleteReceive(connection, ref state, frame);
            }
        }
    }

    [DoesNotReturn]
    private static void FailTimeout(
        TcpConnection connection,
        ref FrameReceiveOperationState state)
    {
        Exception error;
        try
        {
            error = FrameReadTimeoutSource.CreateTimeoutException(
                connection.FrameReadIdleTimeout);
        }
        catch (Exception creationError)
        {
            error = creationError;
        }

        FailReceive(connection, ref state, error);
    }

    private static RpcFrame CompleteReceive(
        TcpConnection connection,
        ref FrameReceiveOperationState state,
        RpcFrame frame)
    {
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch
        {
            frame.Dispose();
            throw;
        }

        return frame;
    }

    [DoesNotReturn]
    private static void FailReceive(
        TcpConnection connection,
        ref FrameReceiveOperationState state,
        Exception error)
    {
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }

        ExceptionDispatchInfo.Capture(error).Throw();
    }
}
