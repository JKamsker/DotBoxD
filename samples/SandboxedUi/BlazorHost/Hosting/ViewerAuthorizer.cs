using System.Security.Claims;
using DotBoxD.UI.Blazor;
using DotBoxD.UI.Runtime;

namespace Examples.SandboxedUi.BlazorHost;

// Demo host identity only. Production hosts supply their authenticated browser principal and policy.
internal sealed class ViewerAuthorizer(Guid sessionId, string viewer) : IUiInteractionAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(session.Id == sessionId && user.Identity?.IsAuthenticated == true && user.Identity.Name == viewer);
    }
}
