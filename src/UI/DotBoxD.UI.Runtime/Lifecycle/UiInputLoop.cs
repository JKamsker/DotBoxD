using System.Diagnostics;

namespace DotBoxD.UI.Runtime;

internal static class UiInputLoop
{
    public static async Task RunAsync(UiSession session, IUiInputSource source, UiPolicy policy, IReadOnlySet<int> remoteEvents, CancellationToken token)
    {
        var window = Stopwatch.GetTimestamp();
        var count = 0;
        var pending = new UiInputConcurrency(policy.MaxInFlightRemoteEvents);
        try
        {
            while (!token.IsCancellationRequested)
            {
                var input = await source.ReadAsync(token).ConfigureAwait(false);
                if (Stopwatch.GetElapsedTime(window) >= TimeSpan.FromSeconds(1))
                { window = Stopwatch.GetTimestamp(); count = 0; }
                if (++count > policy.MaxInputEventsPerSecond)
                { throw new UiValidationException("UI input rate limit exceeded."); }
                if (remoteEvents.Contains(input.EventId))
                {
                    if (pending.TryBegin())
                    { _ = ObserveRemoteAsync(session, ApplyAsync(session, input, token), pending, token); }
                    else
                    { session.RecordInputError("UI remote input concurrency limit exceeded."); }
                }
                else
                { await ApplyLocalAsync(session, input, token).ConfigureAwait(false); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task ApplyLocalAsync(UiSession session, UiInput input, CancellationToken token)
    {
        try
        { await ApplyAsync(session, input, token).ConfigureAwait(false); }
        catch (UiValidationException error) { session.RecordInputError(error.Message); }
    }

    private static async Task ObserveRemoteAsync(UiSession session, ValueTask<UiSnapshot> operation, UiInputConcurrency pending, CancellationToken token)
    {
        try
        { await operation.ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (UiValidationException error) { session.RecordInputError(error.Message); }
        catch { await session.DisposeAsync().ConfigureAwait(false); }
        finally { pending.End(); }
    }

    private static ValueTask<UiSnapshot> ApplyAsync(UiSession session, UiInput input, CancellationToken token)
    {
        if (input.EventId > 0 && input.NodeId == 0 && input.PropertyId == default && input.Value is null)
        { return session.DispatchAsync(input.EventId, token); }
        if (input.EventId == 0 && input.NodeId > 0 && input.Value is not null)
        { return session.SetInputAsync(input.NodeId, input.PropertyId, input.Value, token); }
        throw new UiValidationException("Malformed semantic UI input.");
    }
}

internal sealed class UiInputConcurrency(int maximum)
{
    private int _count;
    public bool TryBegin()
    {
        if (Interlocked.Increment(ref _count) <= maximum)
        { return true; }
        Interlocked.Decrement(ref _count);
        return false;
    }
    public void End() => Interlocked.Decrement(ref _count);
}
