using DotBoxD.Services.Transport;
using Xunit;

namespace DotBoxD.Services.Tests.Transport.Send.StreamPooling;

[Collection(StreamSendOperationCollection.Name)]
public sealed class StreamFrameSendStageTests
{
    private static readonly AsyncLocal<string?> Context = new();
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    [Theory]
    [InlineData((int)PendingStage.Gate)]
    [InlineData((int)PendingStage.Write)]
    [InlineData((int)PendingStage.Flush)]
    public async Task PendingStage_CompletesAndReleasesOwnership(int stageValue)
    {
        var stage = (PendingStage)stageValue;
        var pendingStages = stage switch
        {
            PendingStage.Write => PendingStreamSendStage.Write,
            PendingStage.Flush => PendingStreamSendStage.Flush,
            _ => PendingStreamSendStage.None,
        };
        await using var stream = new ControlledPendingSendStream(pendingStages);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var gateHeldByTest = stage == PendingStage.Gate;
        if (gateHeldByTest)
        {
            Assert.True(connection.SendGate.Wait(0));
        }

        using var cancellation = new CancellationTokenSource();
        var frame = StreamSendTestFrames.Create(10 + (int)stage, out var expected);
        var send = connection.SendFrameValueAsync(frame, cancellation.Token);
        Assert.False(send.IsCompleted);
        Assert.Equal(expected, frame.WrittenMemory.ToArray());

        switch (stage)
        {
            case PendingStage.Gate:
                connection.SendGate.Release();
                gateHeldByTest = false;
                break;
            case PendingStage.Write:
                await stream.WriteEntered.WaitAsync(Guard);
                stream.CompleteWrite();
                break;
            case PendingStage.Flush:
                await stream.FlushEntered.WaitAsync(Guard);
                stream.CompleteFlush();
                break;
        }

        try
        {
            await send.AsTask().WaitAsync(Guard);
        }
        finally
        {
            if (gateHeldByTest)
            {
                connection.SendGate.Release();
            }
        }

        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(1, connection.SendGate.CurrentCount);
        Assert.Equal(expected, stream.WrittenBytes);
        Assert.Equal(cancellation.Token, stream.WriteToken);
        Assert.Equal(cancellation.Token, stream.FlushToken);
        if (stage == PendingStage.Write)
        {
            Assert.Equal(1, stream.WriteResultCount);
        }
    }

    [Fact]
    public async Task EverySuspension_RestoresCallerContextBeforeNextStage()
    {
        var previous = Context.Value;
        Context.Value = "caller";
        try
        {
            await using var stream = new ControlledPendingSendStream(
                PendingStreamSendStage.Write | PendingStreamSendStage.Flush)
            {
                ObserveContext = static () => Context.Value,
            };
            await using var connection = new StreamConnection(stream, ownsStream: false);
            var frame = StreamSendTestFrames.Create(20, out _);
            var send = connection.SendFrameValueAsync(frame);

            await stream.WriteEntered.WaitAsync(Guard);
            await CompleteWithContextAsync(stream.CompleteWrite, "producer");
            await stream.FlushEntered.WaitAsync(Guard);
            Assert.Equal("caller", stream.FlushContext);
            await CompleteWithContextAsync(stream.CompleteFlush, "producer");
            await send.AsTask().WaitAsync(Guard);

            Assert.Equal("caller", stream.WriteContext);
            StreamSendTestFrames.AssertDisposed(frame);
        }
        finally
        {
            Context.Value = previous;
        }
    }

    [Fact]
    public async Task SynchronousStages_ReturnCompletedAndRawSendRemainsUnaffected()
    {
        await using var stream = new ControlledPendingSendStream(PendingStreamSendStage.None);
        await using var connection = new StreamConnection(stream, ownsStream: false);
        var frame = StreamSendTestFrames.Create(30, out var expected);

        var ownedSend = connection.SendFrameValueAsync(frame);

        Assert.True(ownedSend.IsCompletedSuccessfully);
        await ownedSend;
        StreamSendTestFrames.AssertDisposed(frame);
        Assert.Equal(expected, stream.WrittenBytes);

        var rawSend = connection.SendValueAsync(expected);
        Assert.True(rawSend.IsCompletedSuccessfully);
        await rawSend;
        Assert.Equal(2, stream.WriteCount);
    }

    private static Task CompleteWithContextAsync(Action complete, string context) =>
        Task.Run(() =>
        {
            Context.Value = context;
            complete();
        });

    private enum PendingStage
    {
        Gate,
        Write,
        Flush,
    }
}
