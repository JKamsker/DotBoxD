using System.Security.Claims;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor;

/// <summary>Trusted browser-user policy, independent of the plugin's sandbox capability grants.</summary>
public interface IUiInteractionAuthorizer
{
    ValueTask<bool> AuthorizeAsync(
        ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken);
}
