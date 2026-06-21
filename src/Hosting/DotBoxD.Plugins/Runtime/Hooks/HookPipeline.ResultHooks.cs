using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins.Runtime;

// The result-returning hook surface of HookPipeline<TEvent>: the .Register(...) / .RegisterLocal(...) terminals
// (lowered by the analyzer), the generated install entrypoints the interceptors call, and FireResultAsync the
// host calls to dispatch. The actual ordered dispatch + abstain/fallthrough logic lives in ResultHookSlot; this
// partial is the thin pipeline facade over it, kept separate so the notification surface stays focused.
public sealed partial class HookPipeline<TEvent>
{
    private readonly Hooks.ResultHookSlot<TEvent> _resultHooks;

    // The authoring terminals constrain TResult to `struct` only — NOT IHookResult. IHookResult is added to
    // [HookResult] records by the same generator pass, so it is not yet present on the pre-generation
    // compilation the analyzer binds these calls against; constraining on it here would make the call fail to
    // resolve during lowering. The generated interceptor's install entrypoints (below) carry the full
    // `struct, IHookResult` constraint, checked against the post-generation compilation.

    /// <summary>
    /// The result-returning terminal the analyzer lowers to verified IR: the filter and the result-producing
    /// handler both run in the sandbox. Un-lowered it throws, so plugin logic never executes unsandboxed.
    /// </summary>
    public HookPipeline<TEvent> Register<TResult>(Func<TEvent, TResult> handler, int priority = 0)
        where TResult : struct
        => throw Hooks.HookLowering.ResultNotLowered();

    /// <summary>
    /// The result-returning local terminal: the analyzer lowers the filter to verified IR, but the result is
    /// produced by the plugin-process delegate. Un-lowered it throws; the generated interceptor replaces it.
    /// </summary>
    public HookPipeline<TEvent> RegisterLocal<TResult>(Func<TEvent, HookContext, TResult> handler, int priority = 0)
        where TResult : struct
        => throw Hooks.HookLowering.ResultNotLowered();

    public HookPipeline<TEvent> RegisterLocal<TResult>(
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct
        => throw Hooks.HookLowering.ResultNotLowered();

    /// <summary>
    /// Installs a lowered <c>Register</c> chain: the package's verified <c>ShouldHandle</c> filter and
    /// result-producing <c>Handle</c> run in the sandbox, and the returned value is decoded to
    /// <typeparamref name="TResult"/>. Called by the generated interceptor that replaces a
    /// <c>Register(lambda, priority)</c> call site.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedResultChain<TResult>(PluginPackage package, int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        var kernel = MaterializeResultKernel(package);
        try
        {
            _resultHooks.AddSandbox(kernel, priority, Hooks.ResultHookSlot<TEvent>.Decoder<TResult>());
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }

        return this;
    }

    /// <summary>
    /// Installs a lowered <c>RegisterLocal</c> chain: the package's verified <c>ShouldHandle</c> filter runs in
    /// the sandbox, and only when it matches is the native <paramref name="handler"/> invoked to produce the
    /// result. Called by the generated interceptor that replaces a <c>RegisterLocal(lambda, priority)</c> site.
    /// </summary>
    public HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, TResult> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(handler);
        return UseGeneratedLocalResultChain<TResult>(package, (e, context, _) => new ValueTask<TResult>(handler(e, context)), priority);
    }

    public HookPipeline<TEvent> UseGeneratedLocalResultChain<TResult>(
        PluginPackage package,
        Func<TEvent, HookContext, CancellationToken, ValueTask<TResult>> handler,
        int priority = 0)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(handler);
        var kernel = MaterializeResultKernel(package);
        try
        {
            _resultHooks.AddLocal(
                kernel,
                priority,
                async (e, context, ct) => await handler(e, context, ct).ConfigureAwait(false));
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }

        return this;
    }

    /// <summary>
    /// Dispatches result hooks for <paramref name="e"/> in descending priority order and returns the first
    /// successful result, or <see langword="null"/> when none is registered or none succeeds. The host applies
    /// the returned result to its live state.
    /// </summary>
    public ValueTask<TResult?> FireResultAsync<TResult>(TEvent e, CancellationToken cancellationToken = default)
        where TResult : struct, IHookResult
    {
        if (!_resultHooks.HasHandlers)
        {
            return new ValueTask<TResult?>((TResult?)null);
        }

        var context = cancellationToken.CanBeCanceled
            ? new HookContext(_messages, cancellationToken)
            : _defaultContext;
        return _resultHooks.FireAsync<TResult>(e, context, cancellationToken);
    }

    private InstalledKernel MaterializeResultKernel(PluginPackage package)
    {
        if (_installer is null)
        {
            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK063",
                    "this hook pipeline has no installer; create it from a PluginServer to use generated chains.")
            ]);
        }

        var kernel = _installer(package);
        try
        {
            kernel.ValidateFor(_adapter);
            return kernel;
        }
        catch
        {
            _kernels.Remove(kernel);
            throw;
        }
    }
}
