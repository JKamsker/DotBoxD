using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Per-hook-point store and dispatcher for result-returning hooks (<c>.Register(...)</c> /
/// <c>.RegisterLocal(...)</c>) installed on a single <see cref="HookPipeline{TEvent, TContext}"/>. Handlers are kept in
/// a copy-on-write array sorted by descending priority, ties preserving install order. <c>FireAsync</c>
/// walks that order and returns the first <i>successful</i> result: a handler whose filter did not match, or
/// that abstained (<c>Success == false</c>), falls through to the next. A handler that throws is isolated —
/// skipped so one faulty registration cannot break dispatch — and dispatch falls through to the next handler;
/// cancellation of the dispatch token stops the walk.
/// </summary>
internal sealed class ResultHookSlot<TEvent, TContext>
{
    private readonly object _gate = new();
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private readonly ResultHookEntryInvoker<TEvent, TContext> _invoker;
    private readonly Func<long> _nextOrder;
    private volatile Entry[] _entries = [];

    public ResultHookSlot(
        IPluginEventAdapter<TEvent> adapter,
        Action<ResultHookFault>? onFault = null,
        Func<long>? nextOrder = null)
    {
        _adapter = adapter;
        _invoker = new ResultHookEntryInvoker<TEvent, TContext>(onFault);
        _nextOrder = nextOrder ?? NextLocalOrder;
    }

    public bool HasHandlers => _entries.Length > 0;

    internal Entry[] RegistrationEntries => _entries;

    /// <summary>Installs a sandbox <c>Register</c> handler: the kernel's lowered <c>ShouldHandle</c> filter and
    /// result-producing <c>Handle</c> both run in the sandbox, and the returned value is decoded to the result
    /// type. A non-matching filter contributes no result.</summary>
    public void AddSandbox(InstalledKernel kernel, int priority, Func<SandboxValue, IHookResult> decode)
        => Add(priority, kernel, remote: false, async (e, _, _, ct) =>
        {
            var projection = await kernel.InvokeProjectingAsync(_adapter, e, ct).ConfigureAwait(false);
            return projection.Matched ? decode(projection.Value) : null;
        });

    /// <summary>Installs a <c>RegisterLocal</c> handler: the lowered filter runs in the sandbox, and only when it
    /// matches is the plugin-process <paramref name="handler"/> invoked to produce the result.</summary>
    public void AddLocal(
        InstalledKernel filterKernel,
        int priority,
        Func<TEvent, TContext, CancellationToken, ValueTask<IHookResult>> handler)
        => Add(priority, filterKernel, remote: false, async (e, _, context, ct) =>
        {
            var projection = await filterKernel.InvokeProjectingAsync(_adapter, e, ct).ConfigureAwait(false);
            return projection.Matched ? await handler(e, context, ct).ConfigureAwait(false) : null;
        });

    public void AddRemote(
        InstalledKernel filterKernel,
        int priority,
        Func<TEvent, HookContext, TContext, CancellationToken, ValueTask<IHookResult>> handler)
        => Add(
            priority,
            filterKernel,
            remote: true,
            async (e, rawContext, context, ct) => await handler(e, rawContext, context, ct).ConfigureAwait(false),
            async (e, _, _, ct) => await filterKernel.ShouldHandleAsync(_adapter, e, ct).ConfigureAwait(false));

    /// <summary>Installs a handler from a raw invoke delegate. Used by tests to exercise dispatch semantics
    /// without materializing a sandbox kernel; a <see langword="null"/> result means "filter did not match".</summary>
    internal void AddDirect(
        int priority,
        Func<TEvent, TContext, CancellationToken, ValueTask<IHookResult?>> invoke,
        bool remote = false)
        => Add(priority, kernel: null, remote, (e, _, context, ct) => invoke(e, context, ct));

    public async ValueTask<TResult?> FireAsync<TResult>(
        TEvent e,
        HookContext rawContext,
        TContext context,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
        => await FireAsync(e, rawContext, context, ResultHookDispatchOptions<TResult>.Default, cancellationToken)
            .ConfigureAwait(false);

    public async ValueTask<TResult?> FireAsync<TResult>(
        TEvent e,
        HookContext rawContext,
        TContext context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var entries = _entries;
        for (var i = 0; i < entries.Length; i++)
        {
            var result = await FireEntryAsync(entries[i], e, rawContext, context, options, cancellationToken)
                .ConfigureAwait(false);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    internal async ValueTask<TResult?> FireEntryAsync<TResult>(
        Entry entry,
        TEvent e,
        HookContext rawContext,
        TContext context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        cancellationToken.ThrowIfCancellationRequested();

        IHookResult? result;
        try
        {
            if (!await PassesFilterAsync(entry, e, rawContext, context, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            result = await InvokeEntryAsync(entry, e, rawContext, context, options, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SandboxRuntimeException ex) when (cancellationToken.IsCancellationRequested &&
                                                ex.Error.Code == SandboxErrorCode.Cancelled)
        {
            throw new OperationCanceledException(null, ex, cancellationToken);
        }
        catch (Exception ex)
        {
            _invoker.Report(ex);
            return null;
        }

        if (result is null || !result.Success)
        {
            return null;
        }

        if (result is TResult typed)
        {
            return typed;
        }

        _invoker.Report(new InvalidCastException(
            $"Result hook for '{typeof(TEvent).FullName}' returned '{result.GetType().FullName}', " +
            $"but '{typeof(TResult).FullName}' was requested."));
        return null;
    }

    private static async ValueTask<bool> PassesFilterAsync(
        Entry entry,
        TEvent e,
        HookContext rawContext,
        TContext context,
        CancellationToken cancellationToken)
    {
        if (entry.Filter is null)
        {
            return true;
        }

        var matches = await entry.Filter(e, rawContext, context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return matches;
    }

    private ValueTask<IHookResult?> InvokeEntryAsync<TResult>(
        Entry entry,
        TEvent e,
        HookContext rawContext,
        TContext context,
        ResultHookDispatchOptions<TResult> options,
        CancellationToken cancellationToken)
        where TResult : struct, IHookResult
        => entry.Remote
            ? _invoker.InvokeRemoteAsync(entry, e, rawContext, context, options, cancellationToken)
            : entry.Invoke(e, rawContext, context, cancellationToken);

    public bool RemoveKernel(InstalledKernel kernel)
    {
        lock (_gate)
        {
            var remaining = new List<Entry>(_entries.Length);
            foreach (var entry in _entries)
            {
                if (!ReferenceEquals(entry.Kernel, kernel))
                {
                    remaining.Add(entry);
                }
            }

            if (remaining.Count != _entries.Length)
            {
                _entries = [.. remaining];
                return true;
            }

            return false;
        }
    }

    private void Add(
        int priority,
        InstalledKernel? kernel,
        bool remote,
        Func<TEvent, HookContext, TContext, CancellationToken, ValueTask<IHookResult?>> invoke,
        Func<TEvent, HookContext, TContext, CancellationToken, ValueTask<bool>>? filter = null)
    {
        lock (_gate)
        {
            var entry = new Entry(priority, _nextOrder(), kernel, remote, invoke, filter);
            var next = new List<Entry>(_entries.Length + 1);
            next.AddRange(_entries);
            next.Add(entry);
            // Descending priority; equal priority keeps install order (stable on the monotonic Order key).
            next.Sort(static (left, right) => left.Priority != right.Priority
                ? right.Priority.CompareTo(left.Priority)
                : left.Order.CompareTo(right.Order));
            _entries = [.. next];
        }
    }

    private static long NextLocalOrder() => Interlocked.Increment(ref LocalOrder) - 1;

    private static long LocalOrder;

    internal sealed record Entry(
        int Priority,
        long Order,
        InstalledKernel? Kernel,
        bool Remote,
        Func<TEvent, HookContext, TContext, CancellationToken, ValueTask<IHookResult?>> Invoke,
        Func<TEvent, HookContext, TContext, CancellationToken, ValueTask<bool>>? Filter);

    internal static Func<SandboxValue, IHookResult> Decoder(Type resultType)
        => value => (IHookResult)KernelRpcMarshaller.FromSandboxValue(value, resultType)!;

    internal static Func<SandboxValue, IHookResult> Decoder<TResult>()
        where TResult : struct, IHookResult
        => value => (IHookResult)KernelRpcMarshaller.FromSandboxValue(value, typeof(TResult))!;
}
