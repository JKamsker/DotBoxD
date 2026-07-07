using System.Buffers;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;

namespace DotBoxD.Services.Tests.Coverage.Core;

internal sealed class RootOnlyDispatcher : IServiceDispatcher
{
    public string ServiceName => "RootOnly";

    public Task DispatchAsync(
        string method,
        ReadOnlyMemory<byte> payload,
        ISerializer serializer,
        IInstanceRegistry registry,
        IBufferWriter<byte> output,
        CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class ThrowingDisposable : IDisposable
{
    private readonly Exception _error;

    public ThrowingDisposable(Exception error) => _error = error;

    public void Dispose() => throw _error;
}

internal sealed class ThrowingSerializer : ISerializer
{
    public void Serialize<T>(IBufferWriter<byte> writer, T value) => throw new NotSupportedException();

    public T Deserialize<T>(ReadOnlyMemory<byte> data) => throw new NotSupportedException();

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => throw new NotSupportedException();
}
