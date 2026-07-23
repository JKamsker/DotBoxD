using DotBoxD.Services.Buffers;

namespace DotBoxD.Services.Transport;

/// <summary>
/// Marks built-in channels that validate complete frames, serialize every send path, and take
/// ownership when <see cref="IRpcFrameChannel.SendFrameValueAsync"/> returns normally.
/// </summary>
internal interface IValidatedSerialFrameChannel : IRpcFrameChannel
{
}

/// <summary>
/// Carries an owned-frame sender whose complete implementation rejects malformed frames before
/// transfer and takes ownership when it returns normally.
/// </summary>
internal sealed class ValidatedOwnedFrameSender
{
    public ValidatedOwnedFrameSender(
        Func<PooledBufferWriter, CancellationToken, ValueTask> sendAsync) =>
        SendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));

    public Func<PooledBufferWriter, CancellationToken, ValueTask> SendAsync { get; }
}
