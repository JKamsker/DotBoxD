namespace DotBoxD.UI.Runtime;

/// <summary>
/// Trusted connection-bound adapter to the plugin's typed IPC service. It must enforce transport
/// message limits before decoding replies and must never execute plugin handlers in the host.
/// The host retains ownership of the adapter/connection; a session only borrows it.
/// </summary>
public interface IUiRemoteTransport
{
    ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken);
}
