using System.Buffers;
using System.Reflection;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackConstructorReplayFastPathTests
{
    private const int MeasurementIterations = 1000;

    [Fact]
    public void Hot_simple_replay_allocates_only_the_replayed_instance()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new StableDto(42);
        var writer = Warm(serializer, value);

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasurementIterations; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.InRange(allocated, 0, 32L * MeasurementIterations);
    }

    [Fact]
    public void Hot_replay_wraps_getter_failures_with_dto_context()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new GetterThrowingDto(5);
        _ = Warm(serializer, value);
        try
        {
            GetterThrowingDto.ThrowOnRead = true;
            var exception = Assert.Throws<MessagePackSerializationException>(
                () => serializer.SerializeToPayload(value));

            Assert.Contains(nameof(GetterThrowingDto), exception.Message);
            var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal("fast replay getter failed", inner.Message);
        }
        finally
        {
            GetterThrowingDto.ThrowOnRead = false;
        }
    }

    [Fact]
    public void Hot_replay_wraps_constructor_failures_with_dto_context()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ConstructorThrowingDto(5);
        _ = Warm(serializer, value);
        try
        {
            ConstructorThrowingDto.ThrowOnConstruction = true;
            var exception = Assert.Throws<MessagePackSerializationException>(
                () => serializer.SerializeToPayload(value));

            Assert.Contains(nameof(ConstructorThrowingDto), exception.Message);
            var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Equal("fast replay constructor failed", inner.Message);
        }
        finally
        {
            ConstructorThrowingDto.ThrowOnConstruction = false;
        }
    }

    [Fact]
    public void Hot_replay_does_not_cache_a_successful_value()
    {
        var serializer = new MessagePackRpcSerializer();
        var stable = new InstanceSensitiveDto(5, changesValue: false);
        _ = Warm(serializer, stable);
        var changing = new InstanceSensitiveDto(5, changesValue: true);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(changing));

        Assert.Contains("cannot be serialized without changing", exception.Message);
    }

    [Fact]
    public void Hot_replay_preserves_simple_equality_boundaries()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new EqualityBoundaryDto(null, ReplayMode.Active, double.NaN);
        var writer = Warm(serializer, value);

        serializer.Serialize(writer, value);

        Assert.NotEqual(0, writer.WrittenCount);
    }

    [Fact]
    public void Hot_replay_detects_a_later_property_mismatch()
    {
        var serializer = new MessagePackRpcSerializer();
        var stable = new LateMismatchDto(1, 2, changesSecond: false);
        _ = Warm(serializer, stable);
        var changing = new LateMismatchDto(1, 2, changesSecond: true);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(changing));

        Assert.Contains("cannot be serialized without changing", exception.Message);
    }

    [Fact]
    public void Hot_replay_wraps_dto_messagepack_exceptions()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new MessagePackThrowingDto(5);
        _ = Warm(serializer, value);
        try
        {
            MessagePackThrowingDto.ThrowOnRead = true;
            var exception = Assert.Throws<MessagePackSerializationException>(
                () => serializer.SerializeToPayload(value));

            Assert.Contains(nameof(MessagePackThrowingDto), exception.Message);
            var inner = Assert.IsType<MessagePackSerializationException>(exception.InnerException);
            Assert.Equal("user getter failure", inner.Message);
        }
        finally
        {
            MessagePackThrowingDto.ThrowOnRead = false;
        }
    }

    [Fact]
    public void Nonvisible_dto_shape_keeps_reflection_fallback()
    {
        var type = typeof(PrivateDto);
        var constructor = Assert.Single(type.GetConstructors());
        var property = Assert.IsAssignableFrom<PropertyInfo>(type.GetProperty(nameof(PrivateDto.Id)));

        Assert.False(ConstructorReplayValidatorCompiler.CanCompile(
            constructor,
            [property],
            [property]));
    }

    private static ArrayBufferWriter<byte> Warm<T>(MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        for (var i = 0; i < ConstructorReplayValidatorAdmission.SuccessfulReplayThreshold; i++)
        {
            serializer.Serialize(writer, value);
            writer.Clear();
        }

        // Run the newly published dynamic method once before allocation measurement or fault injection.
        serializer.Serialize(writer, value);
        writer.Clear();
        return writer;
    }

    public sealed class StableDto
    {
        public StableDto(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class GetterThrowingDto
    {
        private readonly int _id;

        public GetterThrowingDto(int id) => _id = id;

        public static bool ThrowOnRead { get; set; }

        public int Id => ThrowOnRead
            ? throw new InvalidOperationException("fast replay getter failed")
            : _id;
    }

    public sealed class ConstructorThrowingDto
    {
        public ConstructorThrowingDto(int id)
        {
            if (ThrowOnConstruction)
            {
                throw new InvalidOperationException("fast replay constructor failed");
            }

            Id = id;
        }

        public static bool ThrowOnConstruction { get; set; }

        public int Id { get; }
    }

    public sealed class InstanceSensitiveDto
    {
        public InstanceSensitiveDto(int id, bool changesValue)
        {
            Id = changesValue ? id + 1 : id;
            ChangesValue = changesValue;
        }

        public int Id { get; }

        public bool ChangesValue { get; }
    }

    public sealed class EqualityBoundaryDto
    {
        public EqualityBoundaryDto(string? name, ReplayMode mode, double value)
        {
            Name = name;
            Mode = mode;
            Value = value;
        }

        public string? Name { get; }

        public ReplayMode Mode { get; }

        public double Value { get; }
    }

    public sealed class LateMismatchDto
    {
        public LateMismatchDto(int first, int second, bool changesSecond)
        {
            First = first;
            Second = changesSecond ? second + 1 : second;
            ChangesSecond = changesSecond;
        }

        public int First { get; }

        public int Second { get; }

        public bool ChangesSecond { get; }
    }

    public sealed class MessagePackThrowingDto
    {
        private readonly int _id;

        public MessagePackThrowingDto(int id) => _id = id;

        public static bool ThrowOnRead { get; set; }

        public int Id => ThrowOnRead
            ? throw new MessagePackSerializationException("user getter failure")
            : _id;
    }

    public enum ReplayMode
    {
        Inactive,
        Active,
    }

    private sealed class PrivateDto
    {
        public PrivateDto(int id) => Id = id;

        public int Id { get; }
    }
}
