using DotBoxD.Services.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Transport;

namespace DotBoxD.Transports.Tcp;

/// <summary>Advances one TCP frame send without allocating for completed stages.</summary>
internal static class TcpFrameSendDriver
{
    public static void Initialize(
        TcpConnection connection,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken,
        ref TcpFrameSendState state) =>
        Initialize(connection, frame: null, data, cancellationToken, ref state);

    public static void Initialize(
        TcpConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken,
        ref TcpFrameSendState state) =>
        Initialize(connection, frame, frame.WrittenMemory, cancellationToken, ref state);

    private static void Initialize(
        TcpConnection connection,
        PooledBufferWriter? frame,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken,
        ref TcpFrameSendState state)
    {
        state.Frame = frame;
        state.Connection = connection;
        state.CancellationToken = cancellationToken;
        state.Data = data;

        connection.ThrowIfDisposedForSend();
        cancellationToken.ThrowIfCancellationRequested();
        MessageFramer.ValidateOutgoingFrame(state.Data.Span);
    }

    public static bool TryAdvance(
        ref TcpFrameSendState state,
        out ValueTask pendingOperation)
    {
        var connection = GetConnection(ref state);
        var gateWait = TransportSendGate.WaitAsync(
            connection.SendGate,
            state.CancellationToken);
        if (!gateWait.IsCompleted)
        {
            state.PendingStage = TcpFrameSendStage.Gate;
            pendingOperation = gateWait;
            return false;
        }

        gateWait.GetAwaiter().GetResult();
        state.OwnsGate = true;
        return TryStartWrite(ref state, out pendingOperation);
    }

    public static bool ResumeAfterPending(
        ref TcpFrameSendState state,
        out ValueTask pendingOperation)
    {
        switch (state.PendingStage)
        {
            case TcpFrameSendStage.Gate:
                state.PendingStage = TcpFrameSendStage.None;
                state.OwnsGate = true;
                return TryStartWrite(ref state, out pendingOperation);
            case TcpFrameSendStage.Write:
                state.PendingStage = TcpFrameSendStage.None;
                pendingOperation = default;
                return true;
            default:
                throw new InvalidOperationException(
                    "A TCP frame send resumed without a pending stage.");
        }
    }

    public static Exception? FinishAndClear(ref TcpFrameSendState state)
    {
        var connection = state.Connection;
        var frame = state.Frame;
        var ownsGate = state.OwnsGate;
        state.Clear();

        Exception? cleanupError = null;
        try
        {
            if (ownsGate)
            {
                connection!.ReleaseSendGate();
            }
        }
        catch (Exception error)
        {
            cleanupError = error;
        }
        finally
        {
            try
            {
                frame?.Dispose();
            }
            catch (Exception error)
            {
                cleanupError = error;
            }
        }

        return cleanupError;
    }

    internal static void InitializePendingWriteForTests(
        TcpConnection connection,
        PooledBufferWriter frame,
        CancellationToken cancellationToken,
        ref TcpFrameSendState state)
    {
        Initialize(connection, frame, cancellationToken, ref state);
        state.OwnsGate = true;
        state.PendingStage = TcpFrameSendStage.Write;
    }

    internal static void InitializePendingWriteForTests(
        TcpConnection connection,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken,
        ref TcpFrameSendState state)
    {
        Initialize(connection, data, cancellationToken, ref state);
        state.OwnsGate = true;
        state.PendingStage = TcpFrameSendStage.Write;
    }

    private static bool TryStartWrite(
        ref TcpFrameSendState state,
        out ValueTask pendingOperation)
    {
        var connection = GetConnection(ref state);
        state.CancellationToken.ThrowIfCancellationRequested();
        connection.ThrowIfDisposedForSend();

        var write = connection.SendStream.WriteAsync(
            state.Data,
            state.CancellationToken);
        if (!write.IsCompleted)
        {
            state.PendingStage = TcpFrameSendStage.Write;
            pendingOperation = write;
            return false;
        }

        write.GetAwaiter().GetResult();
        pendingOperation = default;
        return true;
    }

    private static TcpConnection GetConnection(ref TcpFrameSendState state) =>
        state.Connection ?? throw new InvalidOperationException(
            "A TCP frame send advanced without an active connection.");
}
