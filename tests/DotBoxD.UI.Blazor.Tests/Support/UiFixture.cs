using System.Security.Claims;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Blazor.Tests;

internal static class UiFixture
{
    public static UiHost Host(SandboxHost sandbox, UiPolicy? policy = null)
        => new(sandbox, SandboxPolicyBuilder.Create().Build(), policy);
    public static SandboxHost Sandbox() => SandboxHost.Create(b => b.AddDefaultPureBindings());
    public static ClaimsPrincipal User(string name = "alice") => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], "test"));
    public static UiPackage TextPackage(string text = "")
    {
        var builder = new UiBuilder();
        return builder.Build(builder.TextBox(builder.State(text)));
    }
    public static async Task WaitAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        { await Task.Delay(5, timeout.Token); }
    }
}

internal sealed class Authorizer(bool allow = true) : IUiInteractionAuthorizer
{
    public int Calls { get; private set; }
    public ClaimsPrincipal? Principal { get; private set; }
    public UiSession? Session { get; private set; }
    public UiInput? Input { get; private set; }
    public ValueTask<bool> AuthorizeAsync(ClaimsPrincipal user, UiSession session, UiInput input, CancellationToken cancellationToken)
    {
        Calls++;
        Principal = user;
        Session = session;
        Input = input;
        return ValueTask.FromResult(allow);
    }
}
