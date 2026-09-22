namespace DotBoxD.Plugins.Kernel;

public sealed partial class InstalledKernel
{
    private readonly List<Action<InstalledKernel>> _revocationCallbacks = [];
    private int _revocationCancellationCallbackFailed;

    public void Revoke()
    {
        Action<InstalledKernel>[] callbacks = [];
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _revoked, 1) == 0)
            {
                try
                {
                    _revocation.Cancel();
                }
                catch
                {
                    // The token has already transitioned; callback failures cannot roll back revocation.
                    Volatile.Write(ref _revocationCancellationCallbackFailed, 1);
                }

                callbacks = DrainRevocationCallbacks();
            }
        }

        InvokeRevocationCallbacks(callbacks);
    }

    internal void RegisterRevocationCallback(Action<InstalledKernel> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var invokeNow = false;
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _revoked) == 0)
            {
                _revocationCallbacks.Add(callback);
            }
            else
            {
                invokeNow = true;
            }
        }

        if (invokeNow)
        {
            InvokeRevocationCallback(callback);
        }
    }

    private Action<InstalledKernel>[] DrainRevocationCallbacks()
    {
        if (_revocationCallbacks.Count == 0)
        {
            return [];
        }

        var callbacks = _revocationCallbacks.ToArray();
        _revocationCallbacks.Clear();
        return callbacks;
    }

    private void InvokeRevocationCallbacks(Action<InstalledKernel>[] callbacks)
    {
        for (var i = 0; i < callbacks.Length; i++)
        {
            InvokeRevocationCallback(callbacks[i]);
        }
    }

    private void InvokeRevocationCallback(Action<InstalledKernel> callback)
    {
        try
        {
            callback(this);
        }
        catch
        {
            // Revocation cleanup callbacks are best-effort and must not make Revoke fail.
        }
    }
}
