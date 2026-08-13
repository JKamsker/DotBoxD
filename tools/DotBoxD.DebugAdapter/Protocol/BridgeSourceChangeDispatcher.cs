using System.Threading.Channels;

namespace DotBoxD.DebugAdapter;

internal sealed class BridgeSourceChangeDispatcher(
    Func<long, CancellationToken, ValueTask> acknowledge)
{
    private readonly Channel<long> _changes = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly TaskCompletionSource<Func<long, ValueTask>> _receiver =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetReceiver(Func<long, ValueTask> receiver)
    {
        if (!_receiver.TrySetResult(receiver))
        {
            throw new InvalidOperationException("The source change receiver is already configured.");
        }
    }

    public ValueTask EnqueueAsync(long version, CancellationToken cancellationToken)
        => _changes.Writer.WriteAsync(version, cancellationToken);

    public void Complete() => _changes.Writer.TryComplete();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var receiver = await _receiver.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var version in _changes.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await receiver(version).ConfigureAwait(false);
                await acknowledge(version, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Console.Error.WriteLine($"DotBoxD source refresh failed: {exception.GetBaseException().Message}");
            }
        }
    }
}
