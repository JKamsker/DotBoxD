using System.Diagnostics.CodeAnalysis;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;

namespace DotBoxD.Plugins;

using DotBoxD.Kernels;

/// <summary>
/// Server-side owner of the kernels installed over one connection. Each kernel it installs is tagged
/// with this session as its owner, so another session cannot replace or mutate it. Disposing the
/// session — e.g. from a transport disconnect handler — revokes and unregisters every kernel it owns.
/// </summary>
public sealed class PluginSession : IDisposable, IAsyncDisposable
{
    private readonly PluginServer _server;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _ownedInstallIds = new(StringComparer.Ordinal);
    private int _disposed;

    internal PluginSession(PluginServer server) => _server = server;

    /// <summary>
    /// Installs a package owned by this session. The install and the ownership bookkeeping are atomic
    /// with respect to <see cref="Dispose"/>, so a kernel can never be installed-then-orphaned by a
    /// concurrent disconnect.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
            _ownedInstallIds.Add(kernel.InstallId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Installs a <b>server extension</b> package owned by this session (see
    /// <see cref="PluginServer.InstallServerExtensionAsync"/>). Same ownership/atomicity guarantees as
    /// <see cref="InstallAsync"/>; the kernel is invoked request/response via
    /// <see cref="InstalledKernel.InvokeServerExtensionAsync"/> and revoked when the session is disposed.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallServerExtensionAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedServerExtensionAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
            _ownedInstallIds.Add(kernel.InstallId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Installs an event-kernel <paramref name="package"/> owned by this session and wires it in one step,
    /// rolling the install back if validation or wiring fails — the install ceremony every host used to
    /// hand-write. <paramref name="validate"/> runs <b>before</b> install for host route checks;
    /// <paramref name="policy"/> overrides the default install policy; <paramref name="wire"/> is the host's
    /// routing choice (typically <see cref="PluginServer.WireHook"/> or
    /// <see cref="PluginServer.WireSubscription"/> with host wire options).
    /// <para>
    /// The kernel is installed as a non-current instance, wired, and only then promoted to current — all under
    /// the session gate. So: a same-id incumbent is <b>not</b> revoked until wiring succeeds (a wiring failure
    /// rolls the new instance back by its exact install id with the incumbent still live and current), and a
    /// concurrent <see cref="Dispose"/> cannot tear the kernel down mid-wire and leave a dangling handler. On any
    /// failure the original exception is rethrown.
    /// </para>
    /// <para>
    /// Note: during a same-id hot-replace there is a brief window — between wiring the new kernel and promoting it
    /// (which revokes the incumbent) — in which <b>both</b> the outgoing and incoming kernels are wired, since
    /// pipeline handlers are keyed per kernel instance. A concurrent publish of the same event in that window may
    /// be observed by both kernels until promotion completes. (The first install of a given id has no incumbent
    /// and so no such window.)
    /// </para>
    /// </summary>
    /// <remarks>
    /// Opt-in convenience over public primitives: hand-write the equivalent with
    /// <see cref="InstallStagedAsync"/> + your wire action + <see cref="Promote"/> on success /
    /// <see cref="Uninstall"/> on failure if this shape doesn't fit. <b>The <paramref name="wire"/> callback runs
    /// while the session gate is held</b>, so it must only touch server-side routing (e.g.
    /// <see cref="PluginServer.WireHook"/> / <see cref="PluginServer.WireSubscription"/>) and must NOT call back
    /// into this session (<see cref="InstallAsync"/> / <see cref="InstallStagedAsync"/> / <see cref="Promote"/> /
    /// <see cref="Uninstall"/> / <see cref="Owns"/> / <see cref="TryGetOwned"/> / <see cref="UpdateSettingsAsync"/>),
    /// which would deadlock on the non-reentrant gate.
    /// </remarks>
    public async ValueTask<InstalledKernel> InstallAndWireAsync(
        PluginPackage package,
        Action<InstalledKernel> wire,
        Func<PluginPackage, SandboxPolicy>? policy = null,
        Action<PluginPackage>? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(wire);

        validate?.Invoke(package);

        InstalledKernel? staged = null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            // Stage as a non-current instance, wire, then promote — all while holding the gate. Dispose takes the
            // same gate, so it cannot interleave between the install and the wire to revoke the kernel out from
            // under us (it tears the kernel down only after we release), and the incumbent is displaced only once
            // wiring has succeeded.
            staged = await _server.InstallOwnedStagedAsync(this, package, policy?.Invoke(package), cancellationToken)
                .ConfigureAwait(false);
            _ownedInstallIds.Add(staged.InstallId);

            wire(staged);

            _server.PromoteOwned(this, staged);
            return staged;
        }
        catch
        {
            if (staged is not null)
            {
                try
                {
                    // Roll the just-staged instance back by its exact install id. It was never promoted, so the
                    // incumbent (if any) was never revoked and stays current; UninstallOwned also detaches any
                    // handlers wire() managed to register before failing.
                    _ownedInstallIds.Remove(staged.InstallId);
                    _server.UninstallOwned(this, staged.InstallId);
                }
                catch
                {
                    // Best-effort rollback: never let a cleanup failure mask the original install/wire exception,
                    // which is the actionable error and is rethrown below.
                }
            }

            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Installs <paramref name="package"/> owned by this session as a <b>non-current instance</b>: a same-id
    /// incumbent (if any) stays current and un-revoked. Pair with <see cref="Promote"/> once the returned kernel
    /// is wired, or roll it back with <see cref="Uninstall"/> (or by disposing the session). This is the public
    /// staging primitive behind <see cref="InstallAndWireAsync"/> — it lets a host wire a kernel <b>before</b> it
    /// displaces the incumbent, so the helper's behavior is reproducible by hand with public API.
    /// </summary>
    public async ValueTask<InstalledKernel> InstallStagedAsync(
        PluginPackage package,
        SandboxPolicy? policy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var kernel = await _server.InstallOwnedStagedAsync(this, package, policy, cancellationToken).ConfigureAwait(false);
            _ownedInstallIds.Add(kernel.InstallId);
            return kernel;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Promotes a kernel previously installed via <see cref="InstallStagedAsync"/> to be the current kernel for its
    /// plugin id, revoking and detaching any prior same-id incumbent only now (so a pre-promotion failure leaves
    /// the incumbent live). Rejects a kernel this session does not own. A local-terminal kernel has no current-kernel
    /// semantics, so promotion is a no-op for it.
    /// </summary>
    public void Promote(InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_ownedInstallIds.Contains(kernel.InstallId))
            {
                throw new InvalidOperationException(
                    $"install id '{kernel.InstallId}' is not owned by this session and cannot be promoted.");
            }

            _server.PromoteOwned(this, kernel);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Updates live settings for a kernel this session owns. Rejects ids the session does not own so
    /// one plugin cannot tune another plugin's kernel.
    /// </summary>
    public async ValueTask UpdateSettingsAsync(
        string pluginId,
        IReadOnlyDictionary<string, object?> values,
        bool atomic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        ArgumentNullException.ThrowIfNull(values);

        InstalledKernel kernel;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_server.Kernels.TryGet(pluginId, out var ownedKernel) ||
                !ReferenceEquals(ownedKernel.OwnerId, this) ||
                !_ownedInstallIds.Contains(ownedKernel.InstallId))
            {
                throw new SandboxValidationException([
                    new SandboxDiagnostic(
                        "DBXK061",
                        $"plugin id '{pluginId}' is not owned by this session.")
                ]);
            }

            kernel = ownedKernel;
        }
        finally
        {
            _gate.Release();
        }

        await kernel.ModifySettingsAsync(values, atomic, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether this (non-disposed) session currently owns <paramref name="pluginId"/>.</summary>
    public bool Owns(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        _gate.Wait();
        try
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                !_server.Kernels.TryGet(pluginId, out var kernel) ||
                !ReferenceEquals(kernel.OwnerId, this))
            {
                return false;
            }

            return _ownedInstallIds.Contains(kernel.InstallId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Atomically fetches the kernel currently registered under <paramref name="pluginId"/> AND verifies this
    /// session owns it, returning that exact instance. Use this instead of a separate <see cref="Owns"/> + registry
    /// lookup so authorization binds to the kernel actually returned — there is no window in which a same-id
    /// hot-replace could swap a different kernel in between the check and the fetch.
    /// </summary>
    public bool TryGetOwned(string pluginId, [MaybeNullWhen(false)] out InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        _gate.Wait();
        try
        {
            if (Volatile.Read(ref _disposed) == 0 &&
                _server.Kernels.TryGet(pluginId, out var owned) &&
                ReferenceEquals(owned.OwnerId, this) &&
                _ownedInstallIds.Contains(owned.InstallId))
            {
                kernel = owned;
                return true;
            }

            kernel = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Uninstall(string pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        string installId;
        _gate.Wait();
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (!_server.Kernels.TryGet(pluginId, out var kernel) ||
                !ReferenceEquals(kernel.OwnerId, this) ||
                !_ownedInstallIds.Remove(kernel.InstallId))
            {
                return false;
            }

            installId = kernel.InstallId;
        }
        finally
        {
            _gate.Release();
        }

        return _server.UninstallOwned(this, installId);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Wait for any in-flight install to finish (it holds the gate) so the just-installed kernel is
        // captured here and not orphaned, then revoke + unregister everything this session owns.
        string[] owned;
        _gate.Wait();
        try
        {
            owned = [.. _ownedInstallIds];
            _ownedInstallIds.Clear();
        }
        finally
        {
            _gate.Release();
        }

        foreach (var installId in owned)
        {
            _server.UninstallOwned(this, installId);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
