using System.Security.Claims;
using DotBoxD.UI;
using DotBoxD.UI.Blazor;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Examples.SandboxedUi.BlazorHost;

internal static class BlazorSmoke
{
    public static async Task RunAsync(string worker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var alice = await ViewerSession.CreateAsync(worker, token);
        await using var bob = await ViewerSession.CreateAsync(worker, token);
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Alice")], "Demo"));
        var authorizer = new ViewerAuthorizer(alice.Session.Id, "Alice");
        var renderer = (BlazorUiRenderer)alice.Session.Renderer!;
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var html = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var root = await html.Dispatcher.InvokeAsync(() => html.RenderComponentAsync<DotBoxDUi>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DotBoxDUi.Session)] = alice.Session,
                [nameof(DotBoxDUi.User)] = user,
                [nameof(DotBoxDUi.Authorizer)] = authorizer
            })));
        await renderer.SubmitAsync(alice.Session, new UiInput(EventId: 1), user, authorizer, token);
        await WaitAsync(alice.Session, s => s.State[0].Value.Integer == 1, token);
        Require(alice.Transport.Calls == 0 && (await bob.Session.SnapshotAsync(token)).State[0].Value.Integer == 0, "per-viewer local counter");
        await renderer.SubmitAsync(alice.Session, new UiInput(EventId: 3), user, authorizer, token);
        await WaitAsync(alice.Session, s => s.State[3].Value.Integer == 42, token);
        var textBox = alice.Package.Nodes.Single(n => n.Primitive == UiPrimitive.TextBox).Id;
        await renderer.SubmitAsync(alice.Session, new UiInput(NodeId: textBox, PropertyId: UiPropertyId.Text, Value: UiValue.FromString("ap")), user, authorizer, token);
        await WaitAsync(alice.Session, s => s.State[1].Value.Text == "ap", token);
        await renderer.SubmitAsync(alice.Session, new UiInput(EventId: 2), user, authorizer, token);
        await WaitAsync(alice.Session, s => s.State[2].Value.Text == "apple, apricot", token);
        var markup = await html.Dispatcher.InvokeAsync(root.ToHtmlString);
        Require(markup.Contains("apple, apricot", StringComparison.Ordinal) && renderer.Materializations == 1, "generated plugin rendered through Blazor");
        Require(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Examples.SandboxedUi.Plugin"), "plugin assembly absent");
        Require(!await renderer.SubmitAsync(alice.Session, new UiInput(EventId: 1), user, new ViewerAuthorizer(bob.Session.Id, "Alice"), token), "cross-viewer authorization");
        var calls = alice.Transport.Calls;
        await alice.CrashAsync(token);
        Require(alice.Session.IsDisconnected && renderer.IsDisposed && renderer.Snapshot is null && alice.Transport.Calls == calls, "idle disconnect release");
        Require((await bob.Session.DispatchAsync(1, token)).State[0].Value.Integer == 1, "independent live viewer");
        await using var fresh = await ViewerSession.CreateAsync(worker, token);
        Require(fresh.Session.Id != alice.Session.Id, "fresh reconnect identity");
        try
        {
            await fresh.Session.ApplyPatchAsync(new UiStatePatch(alice.Session.Id, 0, []), token);
            throw new InvalidOperationException("Old session patch accepted.");
        }
        catch (UiValidationException) { }
        Console.WriteLine("PASS: same generated package; authorized local counter; two-way input; remote search; player binding; independent viewers; plugin assembly absent; idle disconnect; fresh reconnect.");
    }

    private static async Task WaitAsync(UiSession session, Func<UiSnapshot, bool> condition, CancellationToken token)
    {
        while (!condition(await session.SnapshotAsync(token)))
        { await Task.Delay(5, token); }
    }
    private static void Require(bool condition, string operation)
    { if (!condition) { throw new InvalidOperationException("Blazor smoke failed: " + operation); } }
}
