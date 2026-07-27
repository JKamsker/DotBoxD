using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Serialization;
using MessagePack;
using Xunit;
using BufferPayload = DotBoxD.Services.Buffers.Payload;

namespace DotBoxD.Services.Tests.Protocol.MessagePack;

public sealed class MessagePackComputedDtoRegressionTests
{
    [Fact]
    public void Constructor_only_getter_dto_round_trips()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ConstructorOnlyDto(5);

        using var payload = serializer.SerializeToPayload(value);
        var roundTrip = serializer.Deserialize<ConstructorOnlyDto>(payload.Memory);

        Assert.Equal(value.Id, roundTrip.Id);
    }

    [Fact]
    public void Computed_get_only_dto_round_trip_preserves_serialized_value_or_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ComputedGetOnlyDto(5);

        Assert.Equal(6, value.Id);

        using var payload = TrySerializeOrNull(serializer, value);
        if (payload is null)
        {
            return;
        }

        var roundTrip = serializer.Deserialize<ComputedGetOnlyDto>(payload.Memory);

        Assert.Equal(value.Id, roundTrip.Id);
    }

    [Fact]
    public void Computed_get_only_dto_hidden_as_object_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        object value = new ComputedGetOnlyDto(5);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(value));

        Assert.Contains(
            "cannot be serialized without changing constructor-bound get-only values",
            exception.Message);
    }

    [Fact]
    public void Computed_get_only_dto_hidden_as_interface_fails_closed()
    {
        var serializer = new MessagePackRpcSerializer();
        IComputedDto value = new ComputedGetOnlyDto(5);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(value));

        Assert.Contains(
            "cannot be serialized without changing constructor-bound get-only values",
            exception.Message);
    }

    [Fact]
    public void Constructor_replay_getter_failures_are_wrapped_with_dto_context()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ReplayComparisonThrowingDto(5);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(value));

        Assert.Contains(nameof(ReplayComparisonThrowingDto), exception.Message);
        Assert.Contains("constructor-bound get-only values", exception.Message);

        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal("replay getter failed", inner.Message);
    }

    [Fact]
    public void Successful_constructor_replay_is_not_cached_for_later_values()
    {
        var serializer = new MessagePackRpcSerializer();
        var stable = new InstanceSensitiveDto(5, changesValue: false);
        var changing = new InstanceSensitiveDto(5, changesValue: true);

        using var stablePayload = serializer.SerializeToPayload(stable);
        var stableRoundTrip = serializer.Deserialize<InstanceSensitiveDto>(stablePayload.Memory);
        Assert.Equal(stable.Id, stableRoundTrip.Id);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.SerializeToPayload(changing));
        Assert.Contains(
            "cannot be serialized without changing constructor-bound get-only values",
            exception.Message);
    }

    [Fact]
    public void Constructor_only_dto_hidden_as_base_type_round_trips()
    {
        var serializer = new MessagePackRpcSerializer();
        ConstructorBaseDto value = new ConstructorDerivedDto(5);

        using var payload = serializer.SerializeToPayload(value);
        var roundTrip = serializer.Deserialize<ConstructorBaseDto>(payload.Memory);

        Assert.Equal(value.Id, roundTrip.Id);
    }

    [Fact]
    public void Constructor_guard_metadata_is_safe_during_concurrent_first_use()
    {
        var serializer = new MessagePackRpcSerializer();

        Parallel.For(0, 32, i =>
        {
            var value = new ConcurrentConstructorDto(i);
            using var payload = serializer.SerializeToPayload(value);
            var roundTrip = serializer.Deserialize<ConcurrentConstructorDto>(payload.Memory);
            Assert.Equal(value.Id, roundTrip.Id);
        });
    }

    private static BufferPayload? TrySerializeOrNull(
        MessagePackRpcSerializer serializer,
        ComputedGetOnlyDto value)
    {
        try
        {
            return serializer.SerializeToPayload(value);
        }
        catch (MessagePackSerializationException)
        {
            return null;
        }
    }

    public sealed class ConstructorOnlyDto
    {
        public ConstructorOnlyDto(int id)
        {
            Id = id;
        }

        public int Id { get; }
    }

    public sealed class ReplayComparisonThrowingDto
    {
        private readonly int _id;
        private int _remainingSuccessfulReads = 1;

        public ReplayComparisonThrowingDto(int id)
        {
            _id = id;
        }

        public int Id
        {
            get
            {
                if (_remainingSuccessfulReads > 0)
                {
                    _remainingSuccessfulReads--;
                    return _id;
                }

                throw new InvalidOperationException("replay getter failed");
            }
        }
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

    public class ConstructorBaseDto
    {
        public ConstructorBaseDto(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class ConstructorDerivedDto : ConstructorBaseDto
    {
        public ConstructorDerivedDto(int id)
            : base(id)
        {
        }
    }

    public sealed class ConcurrentConstructorDto
    {
        public ConcurrentConstructorDto(int id) => Id = id;

        public int Id { get; }
    }

    private interface IComputedDto
    {
        int Id { get; }
    }

    public sealed class ComputedGetOnlyDto : IComputedDto
    {
        public ComputedGetOnlyDto(int id)
        {
            Id = id + 1;
        }

        public int Id { get; }
    }
}
