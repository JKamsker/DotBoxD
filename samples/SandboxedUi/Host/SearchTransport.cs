using DotBoxD.UI;
using DotBoxD.UI.Runtime;
using Examples.SandboxedUi.Contracts;

namespace Examples.SandboxedUi.Host;

internal sealed class SearchTransport(IUiPlugin plugin) : IUiRemoteTransport
{
    public int Calls { get; private set; }

    public async ValueTask<UiStatePatch> DispatchAsync(UiRemoteEvent message, CancellationToken cancellationToken)
    {
        Calls++;
        var query = message.Snapshot.State.Single(s => s.SlotId == 2).Value.Text;
        var reply = await plugin.SearchAsync(new SearchRequest(
            message.Snapshot.SessionId, message.Snapshot.Version, message.EndpointId, query), cancellationToken);
        return new UiStatePatch(reply.SessionId, reply.Version, [new UiStateValue(3, UiValue.FromString(reply.Results))]);
    }
}
