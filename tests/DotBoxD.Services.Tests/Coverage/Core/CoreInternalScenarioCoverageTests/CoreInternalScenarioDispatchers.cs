using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class NoopDispatcher : IServiceDispatcher
{
    public const string Service = "Noop";

    public string ServiceName => Service;

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class BlockingDispatcher : IServiceDispatcher
{
    public const string Service = "Round2Blocking";

    private readonly TaskCompletionSource<bool> _firstEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string ServiceName => Service;

    public Task FirstEntered => _firstEntered.Task;

    public async Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default)
    {
        _firstEntered.TrySetResult(true);
        await _release.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    public void Release() => _release.TrySetResult(true);
}
