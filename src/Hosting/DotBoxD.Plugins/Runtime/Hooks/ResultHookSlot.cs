using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Runtime.Hooks;

/// <summary>
/// Per-hook-point store and dispatcher for result-returning hooks (<c>.Register(...)</c> /
/// <c>.RegisterLocal(...)</c>) installed on a single <see cref="HookPipeline{TEvent}"/>. Handlers are kept in
/// a copy-on-write array sorted by descending priority, ties preserving install order. <see cref="FireAsync{TResult}"/>
/// walks that order and returns the first <i>successful</i> result: a handler whose filter did not match, or
/// that abstained (<c>Success == false</c>), falls through to the next. A handler that throws is isolated
/// (logged-by-omission and skipped) so one faulty registration cannot break dispatch; cancellation stops the
/// walk. No registered handler — or none successful — yields <see langword="null"/>.
/// </summary>
internal sealed class ResultHookSlot<TEvent>
{
    private readonly object _gate = new();
    private readonly IPluginEventAdapter<TEvent> _adapter;
    private volatile Entry[] _entries = [];
    private int _order;

    public ResultHookSlot(IPluginEventAdapter<TEvent> adapter)
        => _adapter = adapter;

    public bool HasHandlers => _entries.Length > 0;

    /// <summary>Installs a sandbox <c>Register</c> handler: the kernel's lowered <c>ShouldHandle</c> filter and
    /// result-producing <c>Handle</c> both run in the sandbox, and the returned value is decoded to the result
    /// type. A non-matching filter contributes no result.</summary>
    public void AddSandbox(InstalledKernel kernel, int priority, Func<SandboxValue, IHookResult> decode)
        => Add(priority, kernel, async (e, _, ct) =>
        {
            var projection = await kernel.InvokeProjectingAsync(_adapter, e, ct).ConfigureAwait(false);
            return projection.Matched ? decode(projection.Value) : null;
        });

    /// <summary>Installs a <c>RegisterLocal</c> handler: the lowered filter runs in the sandbox, and only when it
    /// matches is the plugin-process <paramref name="handler"/> invoked to produce the result.</summary>
    public void AddLocal(
        InstalledKernel filterKernel,
        int priority,
        Func<TEvent, HookContext, CancellationToken, ValueTask<IHookResult>> handler)
        => Add(priority, filterKernel, async (e, context, ct) =>
        {
            var projection = await filterKernel.InvokeProjectingAsync(_adapter, e, ct).ConfigureAwait(false);
            return projection.Matched ? await handler(e, context, ct).ConfigureAwait(false) : null;
        });

    /// <summary>Installs a handler from a raw invoke delegate. Used by tests to exercise dispatch semantics
    /// without materializing a sandbox kernel; a <see langword="null"/> result means "filter did not match".</summary>
    internal void AddDirect(
        int priority,
        Func<TEvent, HookContext, CancellationToken, ValueTask<IHookResult?>> invoke)
        => Add(priority, kernel: null, invoke);

    public async ValueTask<TResult?> FireAsync<TResult>(TEvent e, HookContext context, CancellationToken cancellationToken)
        where TResult : struct, IHookResult
    {
        var entries = _entries;
        for (var i = 0; i < entries.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IHookResult? result;
            try
            {
                result = await entries[i].Invoke(e, context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A faulty registration must not break the whole hook point: skip it and continue.
                continue;
            }

            if (result is null || !result.Success)
            {
                continue;
            }

            return (TResult)result;
        }

        return null;
    }

    public void RemoveKernel(InstalledKernel kernel)
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
            }
        }
    }

    private void Add(
        int priority,
        InstalledKernel? kernel,
        Func<TEvent, HookContext, CancellationToken, ValueTask<IHookResult?>> invoke)
    {
        lock (_gate)
        {
            var entry = new Entry(priority, _order++, kernel, invoke);
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

    private sealed record Entry(
        int Priority,
        int Order,
        InstalledKernel? Kernel,
        Func<TEvent, HookContext, CancellationToken, ValueTask<IHookResult?>> Invoke);

    internal static Func<SandboxValue, IHookResult> Decoder<TResult>()
        where TResult : struct, IHookResult
        => value => (TResult)KernelRpcMarshaller.FromSandboxValue(value, typeof(TResult))!;
}
