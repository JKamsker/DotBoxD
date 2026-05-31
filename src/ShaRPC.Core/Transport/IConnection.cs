namespace ShaRPC.Core.Transport;

/// <summary>
/// Represents a bidirectional connection for sending and receiving data. This is the
/// legacy spelling of <see cref="IRpcChannel"/> and adds no members of its own — every
/// connection is already a channel, so it can back an <see cref="ShaRPC.Core.RpcPeer"/>.
/// </summary>
public interface IConnection : IRpcChannel
{
}
