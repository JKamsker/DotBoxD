using DotBoxD.Services.Diagnostics;

namespace DotBoxD.Services.Server;

internal static class InstanceRegistryDisposer
{
    internal static void Dispose(object instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                Task.Run(async () => await asyncDisposable.DisposeAsync().ConfigureAwait(false))
                    .GetAwaiter()
                    .GetResult();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    internal static void DisposeBestEffort(object instance)
    {
        try
        {
            Dispose(instance);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }

    internal static async Task DisposeAsync(object instance)
    {
        switch (instance)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }

    internal static void DisposeAndComplete(
        InstanceRegistryDisposal disposal,
        Action<object> onCompleted,
        bool reportFailure)
    {
        try
        {
            Dispose(disposal.Instance);
            disposal.Completion.SetResult(true);
        }
        catch (Exception ex)
        {
            disposal.Completion.SetException(ex);
            if (reportFailure)
            {
                RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            onCompleted(disposal.Instance);
        }
    }

    internal static async Task DisposeAndCompleteAsync(
        InstanceRegistryDisposal disposal,
        Action<object> onCompleted,
        bool reportFailure)
    {
        try
        {
            await DisposeAsync(disposal.Instance).ConfigureAwait(false);
            disposal.Completion.SetResult(true);
        }
        catch (Exception ex)
        {
            disposal.Completion.SetException(ex);
            if (reportFailure)
            {
                RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            onCompleted(disposal.Instance);
        }
    }

    internal static async Task DisposeAsyncBestEffort(object instance)
    {
        try
        {
            await DisposeAsync(instance).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RpcDiagnostics.Report("Sub-service instance disposal failed", ex);
        }
    }
}
