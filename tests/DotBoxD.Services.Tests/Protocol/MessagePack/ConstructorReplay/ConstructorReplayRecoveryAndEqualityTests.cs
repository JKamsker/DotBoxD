using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayRecoveryAndEqualityTests
{
    [Fact]
    public void Late_polymorphic_mismatch_does_not_poison_later_stable_reuse()
    {
        var serializer = new MessagePackRpcSerializer();
        RecoveryBaseDto stable = new RecoveryDto(1, 2, changesSecond: false);
        var writer = ConstructorReplayTestSupport.Warm(serializer, stable);
        RecoveryBaseDto changing = new RecoveryDto(1, 2, changesSecond: true);

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, changing));
        serializer.Serialize(writer, stable);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(serializer, writer, stable);

        Assert.Contains(nameof(RecoveryDto), exception.Message);
        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(stable.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    [Fact]
    public void Polymorphic_fast_replay_preserves_null_enum_and_nan_equality()
    {
        var serializer = new MessagePackRpcSerializer();
        EqualityBaseDto value = new EqualityDto(null, ReplayMode.Active, double.NaN);
        var writer = ConstructorReplayTestSupport.Warm(serializer, value);

        serializer.Serialize(writer, value);
        Assert.NotEqual(0, writer.WrittenCount);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(serializer, writer, value);

        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    public class RecoveryBaseDto
    {
        public RecoveryBaseDto(int first, int second, bool changesSecond)
        {
            First = first;
            Second = second;
            ChangesSecond = changesSecond;
        }

        public int First { get; }

        public int Second { get; }

        public bool ChangesSecond { get; }
    }

    public sealed class RecoveryDto : RecoveryBaseDto
    {
        public RecoveryDto(int first, int second, bool changesSecond)
            : base(first, changesSecond ? second + 1 : second, changesSecond)
        {
        }
    }

    public enum ReplayMode
    {
        Inactive,
        Active,
    }

    public class EqualityBaseDto
    {
        public EqualityBaseDto(string? name, ReplayMode mode, double value)
        {
            Name = name;
            Mode = mode;
            Value = value;
        }

        public string? Name { get; }

        public ReplayMode Mode { get; }

        public double Value { get; }
    }

    public sealed class EqualityDto : EqualityBaseDto
    {
        public EqualityDto(string? name, ReplayMode mode, double value)
            : base(name, mode, value)
        {
        }
    }
}
