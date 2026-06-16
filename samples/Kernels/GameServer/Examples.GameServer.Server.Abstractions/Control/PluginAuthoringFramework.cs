namespace DotBoxD.Kernels.Game.Server.Abstractions;

// =============================================================================
// FRAMEWORK-PROVIDED — the plugin dev never writes any of this.
//
// In real DotBoxD these live in the framework (DotBoxD.Abstractions for the marker;
// DotBoxD.Plugins.Client for the generic contracts). Inlined here only so the
// vibe-check sample reads end to end.
// =============================================================================

/// <summary>
/// The framework half of the plugin server: lifecycle + the anonymous server-side invokes + hold. It is
/// generic over the game's world-access surface (<typeparamref name="TWorld"/>) so the framework never
/// references a game type. The generated <c>GamePluginServer</c> implements this alongside the game's
/// <c>IGameWorldAccess</c>.
/// </summary>
public interface IPluginServer<TWorld>
    where TWorld : class
{
    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask RunAsync(CancellationToken cancellationToken = default);

    ValueTask<TReturn> InvokeAsync<TReturn>(Func<TWorld, ValueTask<TReturn>> lambda);
    ValueTask<TReturn> InvokeAsync<TCaptures, TReturn>(
        TCaptures captures,
        RemoteServerInvocation<TWorld, TCaptures, TReturn> lambda)
        where TCaptures : class;

    ValueTask HoldUntilShutdownAsync(CancellationToken cancellationToken = default);
}

/// <summary>Lambda shape for the explicit capture-bag <c>InvokeAsync</c> overload.</summary>
public delegate ValueTask<TReturn> RemoteServerInvocation<TWorld, TCaptures, TReturn>(
    TWorld world,
    TCaptures captures);

/// <summary>
/// A control (or the root world surface) that can host plugin-owned server extensions. <c>Extend</c>
/// ships a kernel's verified IR as a server-side batch method grafted onto this control.
/// </summary>
/// <remarks>
/// <c>Extend</c>/<c>Replace</c>/<c>Get</c> ship IR through the host control-plane — they are NOT plain
/// domain RPC. The generated proxy implements them specially; the server handles them via its install
/// endpoints, not as ordinary <c>[DotBoxDService]</c> calls.
/// </remarks>
public interface IExtensibleControl
{
    ValueTask<string> Extend<TService, TKernel>()
        where TService : class
        where TKernel : class;
}

/// <summary>The root service surface: replace a whole server service, or tune an installed kernel.</summary>
public interface IServiceControl : IExtensibleControl
{
    ValueTask<string> Replace<TService, TKernel>()
        where TService : class
        where TKernel : class, TService;

    ILiveSettingsHandle<TKernel> Get<TKernel>()
        where TKernel : class, new();
}

/// <summary>Strongly-typed live-settings tuner for an installed kernel — one atomic IPC batch.</summary>
public interface ILiveSettingsHandle<TKernel>
    where TKernel : class, new()
{
    ValueTask SetValuesAsync(Action<TKernel> set, bool atomic = false);
}

/// <summary>
/// Put on the dev's <c>partial class GamePluginServer : IGameWorldAccess</c>. The generator emits the
/// RPC-proxy implementation of <c>IGameWorldAccess</c>, mixes in <c>IPluginServer&lt;IGameWorldAccess&gt;</c>,
/// and emits a matching <c>GamePluginServerBuilder</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class GeneratePluginServerAttribute : Attribute;
