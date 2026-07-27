using System.Buffers;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayIsolationTests
{
    [Fact]
    public void Base_validator_cannot_overwrite_a_derived_runtime_validator()
    {
        var serializer = new MessagePackRpcSerializer();
        var stableDerived = new OverwriteDerivedDto(7, 11, changesMarker: false);
        _ = ConstructorReplayTestSupport.Warm(serializer, stableDerived);
        _ = ConstructorReplayTestSupport.Warm(serializer, new OverwriteBaseDto(7));
        OverwriteBaseDto changingDerived = new OverwriteDerivedDto(7, 11, changesMarker: true);
        var writer = new ArrayBufferWriter<byte>();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, changingDerived));

        Assert.Contains(nameof(OverwriteDerivedDto), exception.Message);
        Assert.Contains("cannot be serialized without changing", exception.Message);
    }

    [Fact]
    public void Concurrent_exact_and_base_admission_publish_once_and_reuse_safely()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ConcurrentAdmissionDto(42);
        var initialWriter = new ArrayBufferWriter<byte>();
        serializer.Serialize(initialWriter, value);
        PrepareNextReplayToPublish(value.GetType());

        Parallel.Invoke(
            () => SerializeExact(serializer, value),
            () => SerializeBase(serializer, value));

        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Parallel.For(
            0,
            Environment.ProcessorCount * 2,
            worker => ReuseConcurrently(serializer, value, worker));
    }

    private static void PrepareNextReplayToPublish(Type runtimeType)
    {
        var guard = ConstructorReplayTestSupport.GetGuard(runtimeType);
        GetGuardField("_successfulReplays").SetValue(
            guard,
            ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold - 1);
        GetGuardField("_validatorCreationState").SetValue(guard, 0);
        GetGuardField("_validator").SetValue(guard, null);
    }

    private static FieldInfo GetGuardField(string name) =>
        typeof(ConstructorReplayGuard).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"Constructor replay field '{name}' was not found.");

    private static void SerializeExact(
        MessagePackRpcSerializer serializer,
        ConcurrentAdmissionDto value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        Assert.NotEqual(0, writer.WrittenCount);
    }

    private static void SerializeBase(
        MessagePackRpcSerializer serializer,
        ConcurrentAdmissionDto value)
    {
        var writer = new ArrayBufferWriter<byte>();
        ConcurrentAdmissionBaseDto asBase = value;
        serializer.Serialize(writer, asBase);
        Assert.NotEqual(0, writer.WrittenCount);
    }

    private static void ReuseConcurrently(
        MessagePackRpcSerializer serializer,
        ConcurrentAdmissionDto value,
        int worker)
    {
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < 256; i++)
        {
            if ((worker & 1) == 0)
            {
                serializer.Serialize(writer, value);
            }
            else
            {
                ConcurrentAdmissionBaseDto asBase = value;
                serializer.Serialize(writer, asBase);
            }

            Assert.NotEqual(0, writer.WrittenCount);
            writer.Clear();
        }
    }

    public class OverwriteBaseDto
    {
        public OverwriteBaseDto(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class OverwriteDerivedDto : OverwriteBaseDto
    {
        public OverwriteDerivedDto(int id, int marker, bool changesMarker)
            : base(id)
        {
            Marker = changesMarker ? marker + 1 : marker;
            ChangesMarker = changesMarker;
        }

        public int Marker { get; }

        public bool ChangesMarker { get; }
    }

    public class ConcurrentAdmissionBaseDto
    {
        public ConcurrentAdmissionBaseDto(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class ConcurrentAdmissionDto : ConcurrentAdmissionBaseDto
    {
        public ConcurrentAdmissionDto(int id) : base(id) { }
    }
}
