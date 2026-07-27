using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayPolymorphicSharingTests
{
    [Fact]
    public void Base_warmup_publishes_validator_reused_by_exact_declaration()
    {
        var serializer = new MessagePackRpcSerializer();
        BaseWarmBase value = new BaseWarmDto(42);
        var writer = ConstructorReplayTestSupport.Warm(serializer, value);

        serializer.Serialize(writer, (BaseWarmDto)value);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(
            serializer,
            writer,
            (BaseWarmDto)value);

        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    [Fact]
    public void Exact_warmup_publishes_validator_reused_by_base_declaration()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ExactWarmDto(42);
        var writer = ConstructorReplayTestSupport.Warm(serializer, value);
        ExactWarmBase asBase = value;

        serializer.Serialize(writer, asBase);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(serializer, writer, asBase);

        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    [Fact]
    public void Interface_warmup_publishes_validator_reused_by_exact_declaration()
    {
        var serializer = new MessagePackRpcSerializer();
        InterfaceWarmContract value = new InterfaceWarmDto(42);
        var writer = ConstructorReplayTestSupport.Warm(serializer, value);

        serializer.Serialize(writer, (InterfaceWarmDto)value);
        var roundTrip = serializer.Deserialize<InterfaceWarmDto>(writer.WrittenMemory);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(
            serializer,
            writer,
            (InterfaceWarmDto)value);

        Assert.Equal(42, roundTrip.Id);
        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    [Fact]
    public void Exact_warmup_publishes_validator_reused_by_interface_declaration()
    {
        var serializer = new MessagePackRpcSerializer();
        var value = new ExactToInterfaceDto(42);
        var writer = ConstructorReplayTestSupport.Warm(serializer, value);
        ExactToInterfaceContract asInterface = value;

        serializer.Serialize(writer, asInterface);
        var roundTrip = serializer.Deserialize<ExactToInterfaceContract>(writer.WrittenMemory);
        writer.Clear();
        var allocated = ConstructorReplayTestSupport.MeasureAllocated(
            serializer,
            writer,
            asInterface);

        Assert.Equal(42, Assert.IsType<ExactToInterfaceDto>(roundTrip).Id);
        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(value.GetType()));
        Assert.InRange(
            allocated,
            0,
            32L * ConstructorReplayTestSupport.AllocationIterations);
    }

    [Fact]
    public void Alternating_sibling_runtime_types_keep_independent_validators()
    {
        var serializer = new MessagePackRpcSerializer();
        SiblingBase first = new FirstSiblingDto(1);
        SiblingBase second = new SecondSiblingDto(2);
        var writer = ConstructorReplayTestSupport.Warm(serializer, first);
        _ = ConstructorReplayTestSupport.Warm(serializer, second);

        serializer.Serialize(writer, first);
        writer.Clear();
        serializer.Serialize(writer, second);
        writer.Clear();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < ConstructorReplayTestSupport.AllocationIterations; i++)
        {
            serializer.Serialize(writer, first);
            writer.Clear();
            serializer.Serialize(writer, second);
            writer.Clear();
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(first.GetType()));
        Assert.NotNull(ConstructorReplayTestSupport.GetValidator(second.GetType()));
        Assert.InRange(
            allocated,
            0,
            64L * ConstructorReplayTestSupport.AllocationIterations);
    }

    public class BaseWarmBase
    {
        public BaseWarmBase(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class BaseWarmDto : BaseWarmBase
    {
        public BaseWarmDto(int id) : base(id) { }
    }

    public class ExactWarmBase
    {
        public ExactWarmBase(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class ExactWarmDto : ExactWarmBase
    {
        public ExactWarmDto(int id) : base(id) { }
    }

    [Union(0, typeof(InterfaceWarmDto))]
    public interface InterfaceWarmContract
    {
        int Id { get; }
    }

    [MessagePackObject]
    public sealed class InterfaceWarmDto : InterfaceWarmContract
    {
        public InterfaceWarmDto(int id) => Id = id;

        [Key(0)]
        public int Id { get; }
    }

    [Union(0, typeof(ExactToInterfaceDto))]
    public interface ExactToInterfaceContract
    {
        int Id { get; }
    }

    [MessagePackObject]
    public sealed class ExactToInterfaceDto : ExactToInterfaceContract
    {
        public ExactToInterfaceDto(int id) => Id = id;

        [Key(0)]
        public int Id { get; }
    }

    public class SiblingBase
    {
        public SiblingBase(int id) => Id = id;

        public int Id { get; }
    }

    public sealed class FirstSiblingDto : SiblingBase
    {
        public FirstSiblingDto(int id) : base(id) { }
    }

    public sealed class SecondSiblingDto : SiblingBase
    {
        public SecondSiblingDto(int id) : base(id) { }
    }
}
