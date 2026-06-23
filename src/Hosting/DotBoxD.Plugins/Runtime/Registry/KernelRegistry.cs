using System.Collections;
using System.Diagnostics.CodeAnalysis;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins.Kernel;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins;

public sealed class KernelRegistry : IEnumerable<InstalledKernel>
{
    private readonly object _gate = new();
    private readonly Dictionary<string, InstalledKernel> _currentByPluginId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InstalledKernel> _kernelsByInstallId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<InstalledKernel>> _kernelsByPluginId = new(StringComparer.Ordinal);

    public InstalledKernel Get(string pluginId)
    {
        lock (_gate)
        {
            return _currentByPluginId[pluginId];
        }
    }

    public TypedInstalledKernel<TState> Get<TState>(string pluginId) where TState : class
        => new(Get(pluginId));

    /// <summary>
    /// Probes installation state without throwing, letting an admin/host UI discover whether a
    /// plugin id is currently installed and read its live kernel without catching
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    public bool TryGet(string pluginId, [MaybeNullWhen(false)] out InstalledKernel kernel)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            return _currentByPluginId.TryGetValue(pluginId, out kernel);
        }
    }

    /// <summary>
    /// Returns a stable snapshot of the currently installed kernels for inventory rendering. The
    /// returned list is detached from registry internals, so it is safe to enumerate while installs
    /// and uninstalls continue concurrently.
    /// </summary>
    public IReadOnlyList<InstalledKernel> Snapshot()
    {
        lock (_gate)
        {
            return _kernelsByInstallId.Values.ToArray();
        }
    }

    /// <summary>
    /// Enumerates the currently installed kernels over a stable snapshot, so an admin/host UI can
    /// iterate the inventory directly (for example with <c>foreach</c> or LINQ) without taking a
    /// dependency on <see cref="Snapshot"/>. Enumeration is detached from registry internals and is
    /// therefore unaffected by concurrent installs and uninstalls.
    /// </summary>
    public IEnumerator<InstalledKernel> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal InstalledKernel GetByKernelType<TKernel>() where TKernel : class
    {
        var pluginId = KernelTypeMetadata.PluginId(typeof(TKernel));
        return Get(pluginId);
    }

    internal InstalledKernel? Add(InstalledKernel kernel)
    {
        InstalledKernel? revoke = null;
        lock (_gate)
        {
            EnsureOwnerCanUsePrincipal(kernel);
            if (_currentByPluginId.TryGetValue(kernel.Manifest.PluginId, out var existing) &&
                !ReferenceEquals(existing, kernel))
            {
                revoke = existing;
                RemoveCore(existing);
            }

            AddCore(kernel, makeCurrent: true);
        }

        revoke?.Revoke();
        return revoke;
    }

    internal void AddInstance(InstalledKernel kernel)
    {
        lock (_gate)
        {
            EnsureOwnerCanUsePrincipal(kernel);
            AddCore(kernel, makeCurrent: false);
        }
    }

    /// <summary>
    /// Removes and revokes a kernel instance only if it is owned by <paramref name="owner"/> (or has no owner),
    /// so a session disposal never tears down another session's same-principal kernel.
    /// </summary>
    internal InstalledKernel? RemoveOwned(PluginSession owner, string installId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_kernelsByInstallId.TryGetValue(installId, out kernel))
            {
                return null;
            }

            if (kernel.OwnerId is not null && !ReferenceEquals(kernel.OwnerId, owner))
            {
                return null;
            }

            RemoveCore(kernel);
        }

        kernel.Revoke();
        return kernel;
    }

    internal InstalledKernel? Remove(string pluginId)
    {
        InstalledKernel? kernel;
        lock (_gate)
        {
            if (!_currentByPluginId.TryGetValue(pluginId, out kernel))
            {
                return null;
            }

            RemoveCore(kernel);
        }

        if (kernel is null)
        {
            return null;
        }

        kernel.Revoke();
        return kernel;
    }

    internal InstalledKernel? Remove(InstalledKernel kernel)
    {
        lock (_gate)
        {
            if (!_kernelsByInstallId.TryGetValue(kernel.InstallId, out var current) ||
                !ReferenceEquals(current, kernel))
            {
                return null;
            }

            RemoveCore(kernel);
        }

        kernel.Revoke();
        return kernel;
    }

    private void AddCore(InstalledKernel kernel, bool makeCurrent)
    {
        if (_kernelsByInstallId.ContainsKey(kernel.InstallId))
        {
            throw new InvalidOperationException($"kernel install id '{kernel.InstallId}' is already registered.");
        }

        _kernelsByInstallId[kernel.InstallId] = kernel;
        if (!_kernelsByPluginId.TryGetValue(kernel.Manifest.PluginId, out var principals))
        {
            principals = [];
            _kernelsByPluginId[kernel.Manifest.PluginId] = principals;
        }

        principals.Add(kernel);
        if (makeCurrent)
        {
            _currentByPluginId[kernel.Manifest.PluginId] = kernel;
        }
    }

    private void RemoveCore(InstalledKernel kernel)
    {
        _kernelsByInstallId.Remove(kernel.InstallId);
        if (_currentByPluginId.TryGetValue(kernel.Manifest.PluginId, out var current) &&
            ReferenceEquals(current, kernel))
        {
            _currentByPluginId.Remove(kernel.Manifest.PluginId);
        }

        if (_kernelsByPluginId.TryGetValue(kernel.Manifest.PluginId, out var principals))
        {
            principals.Remove(kernel);
            if (principals.Count == 0)
            {
                _kernelsByPluginId.Remove(kernel.Manifest.PluginId);
            }
        }
    }

    private void EnsureOwnerCanUsePrincipal(InstalledKernel kernel)
    {
        if (!_kernelsByPluginId.TryGetValue(kernel.Manifest.PluginId, out var existingKernels))
        {
            return;
        }

        foreach (var existing in existingKernels)
        {
            if (ReferenceEquals(existing, kernel) ||
                ReferenceEquals(existing.OwnerId, kernel.OwnerId) ||
                (existing.OwnerId is null && kernel.OwnerId is null))
            {
                continue;
            }

            throw new SandboxValidationException([
                new SandboxDiagnostic(
                    "DBXK060",
                    $"plugin id '{kernel.Manifest.PluginId}' is owned by another session and cannot be replaced.")
            ]);
        }
    }
}
