namespace DotBoxD.Services.Transport;

/// <summary>
/// Marks built-in channels that validate complete frames, serialize every send path, and take
/// ownership when <see cref="IRpcFrameChannel.SendFrameValueAsync"/> returns normally.
/// </summary>
internal interface IValidatedSerialFrameChannel : IRpcFrameChannel
{
}
