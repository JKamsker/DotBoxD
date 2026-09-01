namespace DotBoxD.Services.Server;

/// <summary>
/// Per-connection registry that holds server-side sub-service instances by opaque
/// instance identifier. Created by the server when a connection is accepted and
/// drained when the connection closes — instances therefore have connection-scoped
/// lifetime and cannot leak across tenants.
/// </summary>
public interface IInstanceRegistry
{
    /// <summary>
    /// Registers an instance under <paramref name="serviceName"/> and returns the
    /// freshly allocated identifier the client will quote on subsequent calls.
    /// <paramref name="serviceName"/> must be non-empty and non-whitespace.
    /// </summary>
    string Register(string serviceName, object instance);

    /// <summary>
    /// Looks up an instance previously registered under
    /// (<paramref name="serviceName"/>, <paramref name="instanceId"/>). Invalid or unknown keys
    /// return <see langword="false"/>.
    /// </summary>
    bool TryGet(string serviceName, string instanceId, out object instance);

    /// <summary>
    /// Acquires an instance for an in-flight operation. Dispose <paramref name="lease"/> after the
    /// operation completes so a concurrent release cannot dispose the instance while it is in use.
    /// The default implementation preserves the lookup behavior for custom registries that do not
    /// own instance lifetimes.
    /// </summary>
    bool TryAcquire(string serviceName, string instanceId, out object instance, out IAsyncDisposable lease)
    {
        if (TryGet(serviceName, instanceId, out instance))
        {
            lease = EmptyInstanceLease.Instance;
            return true;
        }

        lease = null!;
        return false;
    }

    /// <summary>
    /// Releases an instance early (the connection-teardown path also clears it). Keys must be
    /// non-empty and non-whitespace.
    /// </summary>
    void Release(string serviceName, string instanceId);

    /// <summary>
    /// Releases an instance early and awaits async disposal when no in-flight operation still holds it. Keys must
    /// be non-empty and non-whitespace.
    /// </summary>
    ValueTask ReleaseAsync(string serviceName, string instanceId);

    /// <summary>Removes every entry — called from the connection-cleanup path.</summary>
    void ReleaseAll();
}
