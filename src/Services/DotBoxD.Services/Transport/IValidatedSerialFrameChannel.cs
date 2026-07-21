namespace DotBoxD.Services.Transport;

/// <summary>
/// Marks built-in channels that validate complete frames, serialize every send path, and take
/// ownership of a frame before returning from <see cref="IRpcFrameChannel.SendFrameValueAsync"/>.
/// </summary>
internal interface IValidatedSerialFrameChannel : IRpcFrameChannel
{
}
