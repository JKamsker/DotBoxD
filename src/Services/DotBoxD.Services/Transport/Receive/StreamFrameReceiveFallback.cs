using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Transport;

/// <summary>Task-backed overflow when every reusable Stream receive source is active.</summary>
internal static class StreamFrameReceiveFallback
{
    public static ValueTask<RpcFrame> StartFromBeginning(
        StreamConnection connection,
        CancellationToken ct) =>
        ContinueFromBeginningAsync(connection, ct);

    public static ValueTask<RpcFrame> Start(
        StreamConnection connection,
        FrameReceiveOperationState state,
        ValueTask<int> pendingRead) =>
        ContinueAsync(connection, state, pendingRead);

    private static async ValueTask<RpcFrame> ContinueFromBeginningAsync(
        StreamConnection connection,
        CancellationToken ct)
    {
        var owner = new StreamFrameReceiveOwner();
        RpcFrame completedFrame = default;
        try
        {
            connection.ThrowIfDisposedForReceive();
            var readToken = ct;
            var remaining = connection.UseFrameReceiveLookahead
                ? StreamFrameReadOperations.BeginFrame(ref connection.FrameReceiveBuffer)
                : StreamFrameReadOperations.LengthPrefixSize;
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
                        var pendingRead = StreamFrameReceiveFallbackRead.Start(
                            connection,
                            ref owner,
                            remaining,
                            readToken);
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
                            StreamFrameReadProgressFormat.WholeFrame);
                        goto ReceiveCompleted;
                    }

                    if (connection.UseFrameReceiveLookahead)
                    {
                        remaining = StreamFrameReadOperations.CommitRead(
                            ref connection.FrameReceiveBuffer,
                            ref owner,
                            remaining,
                            read);
                    }
                    else
                    {
                        owner.AdvanceBodyRead(
                            read,
                            connection.FrameReceiveBuffer.WriterBackedOwner);
                        remaining -= read;
                    }

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

                remaining = connection.UseFrameReceiveLookahead
                    ? StreamFrameReadOperations.InitializeOwner(
                        ref connection.FrameReceiveBuffer,
                        connection.FrameReceiveLengthBuffer,
                        connection.MaxIncomingFrameSize,
                        connection.FrameReceiveBuffer.WriterBackedOwner,
                        ref owner)
                    : StreamFrameReadOperations.InitializeExactOwner(
                        connection.FrameReceiveLengthBuffer,
                        connection.MaxIncomingFrameSize,
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
        StreamConnection connection,
        ref StreamFrameReceiveOwner owner,
        RpcFrame frame)
    {
        var state = CreateCleanupState(connection, owner);
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch
        {
            frame.Dispose();
            throw;
        }
        finally
        {
            owner = state.Owner;
        }

        return frame;
    }

    [DoesNotReturn]
    private static void FailScalarReceive(
        StreamConnection connection,
        ref StreamFrameReceiveOwner owner,
        Exception error)
    {
        var state = CreateCleanupState(connection, owner);
        try
        {
            connection.FinishFrameReceive(ref state);
        }
        catch (Exception cleanupError)
        {
            error = cleanupError;
        }
        finally
        {
            owner = state.Owner;
        }

        ExceptionDispatchInfo.Capture(error).Throw();
    }

    private static FrameReceiveOperationState CreateCleanupState(
        StreamConnection connection,
        StreamFrameReceiveOwner owner) =>
        new()
        {
            Owner = owner,
            WriterBacked = connection.FrameReceiveBuffer.WriterBackedOwner,
        };

    private static async ValueTask<RpcFrame> ContinueAsync(
        StreamConnection connection,
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
                completed = StreamFrameReceiveDriver.ResumeAfterRead(
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
        StreamConnection connection,
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
        StreamConnection connection,
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
        StreamConnection connection,
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
