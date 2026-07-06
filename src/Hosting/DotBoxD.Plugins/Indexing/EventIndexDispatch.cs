using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Plugins.Indexing;

internal static class EventIndexDispatch
{
    public static async Task DispatchAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        EventIndexRegistry.EventIndexEntry<TEvent> entry,
        TEvent value,
        CancellationToken cancellationToken,
        Action<SubscriptionDeliveryFault>? onFault)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!await ShouldDispatchAsync(adapter, entry, value, cancellationToken, onFault).ConfigureAwait(false))
        {
            return;
        }

        await HandleDispatchAsync(adapter, entry, value, cancellationToken, onFault).ConfigureAwait(false);
    }

    private static async Task<bool> ShouldDispatchAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        EventIndexRegistry.EventIndexEntry<TEvent> entry,
        TEvent value,
        CancellationToken cancellationToken,
        Action<SubscriptionDeliveryFault>? onFault)
    {
        try
        {
            // The verified IR predicate remains the authority; manifest coverage claims are not trusted.
            return (entry.FullyCovered ||
                    await entry.Kernel.ShouldHandleAsync(adapter, value, cancellationToken).ConfigureAwait(false)) &&
                !cancellationToken.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SandboxRuntimeException ex) when (WasCallerCancelled(ex, cancellationToken))
        {
            return false;
        }
        catch (Exception ex)
        {
            Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Filter);
            return false;
        }
    }

    private static async Task HandleDispatchAsync<TEvent>(
        IPluginEventAdapter<TEvent> adapter,
        EventIndexRegistry.EventIndexEntry<TEvent> entry,
        TEvent value,
        CancellationToken cancellationToken,
        Action<SubscriptionDeliveryFault>? onFault)
    {
        try
        {
            await entry.Kernel.HandleAsync(adapter, value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (SandboxRuntimeException ex) when (WasCallerCancelled(ex, cancellationToken))
        {
        }
        catch (Exception ex)
        {
            Report<TEvent>(onFault, ex, SubscriptionDeliveryStage.Handler);
        }
    }

    private static bool WasCallerCancelled(SandboxRuntimeException exception, CancellationToken cancellationToken)
        => cancellationToken.IsCancellationRequested && exception.Error.Code == SandboxErrorCode.Cancelled;

    private static void Report<TEvent>(
        Action<SubscriptionDeliveryFault>? onFault,
        Exception exception,
        SubscriptionDeliveryStage stage)
    {
        if (onFault is null)
        {
            return;
        }

        try
        {
            onFault(new SubscriptionDeliveryFault(typeof(TEvent), stage, exception));
        }
        catch
        {
        }
    }
}
