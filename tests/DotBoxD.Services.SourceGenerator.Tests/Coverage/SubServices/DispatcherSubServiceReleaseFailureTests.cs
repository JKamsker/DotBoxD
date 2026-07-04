using System.Buffers;
using DotBoxD.Services.Protocol;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Server;
using DotBoxD.Services.SourceGenerator.Tests.SubServices;

namespace DotBoxD.Services.SourceGenerator.Tests.Coverage.SubServices;

public sealed class DispatcherSubServiceReleaseFailureTests
{
    private const string Source = """
        using DotBoxD.Services.Attributes;
        using System.Threading.Tasks;

        namespace Coverage.SubServiceReleaseFailure
        {
            [DotBoxDService]
            public interface ISubService
            {
                Task<int> CountAsync();
            }

            [DotBoxDService]
            public interface IRootService
            {
                Task<ISubService> GetSubAsync(string label);
            }
        }
        """;

    [Fact]
    public async Task Dispatcher_preserves_serialization_error_when_release_async_also_fails()
    {
        var (asm, _) = NestedServiceTestCompiler.Compile(Source);
        var dispatcherType = asm.GetType("Coverage.SubServiceReleaseFailure.RootServiceDispatcher")!;
        var rootType = asm.GetType("Coverage.SubServiceReleaseFailure.IRootService")!;
        var subType = asm.GetType("Coverage.SubServiceReleaseFailure.ISubService")!;
        var root = RootImplFactory.Create(rootType, _ => SubImplFactory.Create(subType));
        var dispatcher = (IServiceDispatcher)Activator.CreateInstance(dispatcherType, root)!;
        var registry = new ThrowingReleaseRegistry();
        var serializer = new ThrowingServiceHandleSerializer();
        using var payload = serializer.SerializeToPayload("hello");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dispatcher.DispatchToPayloadAsync(
                "GetSubAsync",
                payload.Memory,
                serializer,
                registry,
                CancellationToken.None));

        Assert.Equal("service handle serialization failed", ex.Message);
        Assert.True(registry.ReleaseAttempted);
    }

    private sealed class ThrowingServiceHandleSerializer : ISerializer
    {
        private readonly TestJsonSerializer _inner = new();

        public void Serialize<T>(IBufferWriter<byte> writer, T value)
        {
            if (value is ServiceHandle)
            {
                throw new InvalidOperationException("service handle serialization failed");
            }

            _inner.Serialize(writer, value);
        }

        public T Deserialize<T>(ReadOnlyMemory<byte> data) => _inner.Deserialize<T>(data);

        public object? Deserialize(ReadOnlyMemory<byte> data, Type type) => _inner.Deserialize(data, type);
    }

    private sealed class ThrowingReleaseRegistry : IInstanceRegistry
    {
        public bool ReleaseAttempted { get; private set; }

        public string Register(string serviceName, object instance) => "sub-1";

        public bool TryGet(string serviceName, string instanceId, out object instance)
        {
            instance = null!;
            return false;
        }

        public void Release(string serviceName, string instanceId)
            => throw new InvalidOperationException("release failed");

        public ValueTask ReleaseAsync(string serviceName, string instanceId)
        {
            ReleaseAttempted = true;
            throw new InvalidOperationException("release failed");
        }

        public void ReleaseAll()
        {
        }
    }
}
