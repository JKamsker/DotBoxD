using System.Security.Claims;
using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace DotBoxD.UI.Blazor;

/// <summary>Trusted Interactive Server/Hybrid component. Each viewer supplies its own UiSession.</summary>
public sealed class DotBoxDUi : ComponentBase, IAsyncDisposable
{
    private UiSession? _session;
    private BlazorUiRenderer? _renderer;
    private IDisposable? _subscription;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    [Parameter, EditorRequired] public UiSession? Session { get; set; }
    /// <summary>Supplied by trusted host authentication code, never by the plugin package.</summary>
    [Parameter] public ClaimsPrincipal User { get; set; } = new();
    /// <summary>Missing authorization denies every browser interaction, including local kernels.</summary>
    [Parameter] public IUiInteractionAuthorizer? Authorizer { get; set; }
    /// <summary>Default per-viewer ownership. Hosts may explicitly retain a detached session.</summary>
    [Parameter] public bool DisposeSessionOnDetach { get; set; } = true;
    public string? LastInteractionError { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_disposed || ReferenceEquals(Session, _session))
        { return; }
        var previous = _session;
        _subscription?.Dispose();
        _subscription = null;
        _session = null;
        _renderer = null;
        if (DisposeSessionOnDetach && previous is not null)
        { await previous.DisposeAsync(); }
        if (Session is null)
        { return; }
        if (Session.IsDisconnected)
        { _session = Session; return; }
        if (Session.Renderer is not BlazorUiRenderer renderer)
        { throw new InvalidOperationException("Install the UiSession with a BlazorUiRenderer before attaching DotBoxDUi."); }
        _subscription = renderer.Subscribe(RefreshAsync);
        _session = Session;
        _renderer = renderer;
        LastInteractionError = null;
    }

    private Task RefreshAsync() => _disposed ? Task.CompletedTask : InvokeAsync(StateHasChanged);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "dotboxd-ui");
        builder.AddAttribute(8, "style", "contain:content;isolation:isolate;overflow:auto;max-width:100%;");
        if (_renderer?.Snapshot is { } snapshot && _session is { } session && !session.IsDisconnected)
        { UiHtmlTree.Render(builder, snapshot, snapshot.Package.RootNodeId, this, input => SubmitAsync(session, input)); }
        else if (_session?.IsDisconnected == true)
        {
            builder.OpenElement(2, "p");
            builder.AddAttribute(3, "role", "status");
            builder.AddContent(4, "Plugin disconnected.");
            builder.CloseElement();
        }
        if (LastInteractionError is { } error)
        {
            builder.OpenElement(5, "p");
            builder.AddAttribute(6, "role", "alert");
            builder.AddContent(7, error);
            builder.CloseElement();
        }
        builder.CloseElement();
    }

    private async Task SubmitAsync(UiSession session, UiInput input)
    {
        if (_disposed || !ReferenceEquals(session, _session))
        { return; }
        try
        {
            if (Authorizer is null || !await _renderer!.SubmitAsync(session, input, User, Authorizer, _lifetime.Token))
            { LastInteractionError = "This interaction is not authorized."; }
            else
            { LastInteractionError = null; }
        }
        catch (UiValidationException error) { LastInteractionError = error.Message; }
        catch (ObjectDisposedException) { LastInteractionError = "Plugin disconnected."; }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || session.IsDisconnected)
        { LastInteractionError = "Plugin disconnected."; }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        { return; }
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
        var session = _session;
        _session = null;
        _renderer = null;
        try
        {
            await _lifetime.CancelAsync();
            if (DisposeSessionOnDetach && session is not null)
            { await session.DisposeAsync(); }
        }
        finally { _lifetime.Dispose(); }
    }
}
