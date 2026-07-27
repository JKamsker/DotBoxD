using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using MessagePack;
using Xunit;

namespace DotBoxD.Services.Tests.Protocol.MessagePack.ConstructorReplay;

public sealed class ConstructorReplayEvaluationOrderTests
{
    [Fact]
    public void Polymorphic_fast_replay_preserves_getter_constructor_and_comparison_order()
    {
        var serializer = new MessagePackRpcSerializer();
        ReplayOrderDto.CurrentSource = null;
        ReplayOrderDto.Reset(ReplayFault.None);
        ReplayOrderBase value = new ReplayOrderDto(1, 2);
        ReplayOrderDto.CurrentSource = (ReplayOrderDto)value;
        _ = ConstructorReplayTestSupport.Warm(serializer, value);
        ReplayOrderDto.Reset(ReplayFault.None);
        var writer = new ArrayBufferWriter<byte>();

        serializer.Serialize(writer, value);

        Assert.Equal(
            [
                "source:first",
                "source:second",
                "constructor",
                "source:first",
                "replay:first",
                "source:second",
                "replay:second",
            ],
            ReplayOrderDto.Events.Take(7));
    }

    [Theory]
    [InlineData(ReplayFault.ArgumentGetter, "source:first")]
    [InlineData(ReplayFault.Constructor, "source:first,source:second,constructor")]
    [InlineData(
        ReplayFault.SourceComparisonGetter,
        "source:first,source:second,constructor,source:first")]
    [InlineData(
        ReplayFault.ReplayComparisonGetter,
        "source:first,source:second,constructor,source:first,replay:first")]
    public void Polymorphic_fast_replay_wraps_each_fault_and_leaves_writer_unchanged(
        ReplayFault fault,
        string expectedEvents)
    {
        var serializer = new MessagePackRpcSerializer();
        ReplayOrderDto.CurrentSource = null;
        ReplayOrderDto.Reset(ReplayFault.None);
        ReplayOrderBase value = new ReplayOrderDto(1, 2);
        ReplayOrderDto.CurrentSource = (ReplayOrderDto)value;
        _ = ConstructorReplayTestSupport.Warm(serializer, value);
        ReplayOrderDto.Reset(fault);
        var writer = new ArrayBufferWriter<byte>();
        writer.Write(new byte[] { 0x91, 0x2a });
        var initialBytes = writer.WrittenSpan.ToArray();

        var exception = Assert.Throws<MessagePackSerializationException>(
            () => serializer.Serialize(writer, value));

        Assert.Contains(nameof(ReplayOrderDto), exception.Message);
        var inner = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Equal($"replay {fault} failed", inner.Message);
        Assert.Equal(expectedEvents.Split(','), ReplayOrderDto.Events);
        Assert.Equal(initialBytes, writer.WrittenSpan.ToArray());
    }

    public enum ReplayFault
    {
        None,
        ArgumentGetter,
        Constructor,
        SourceComparisonGetter,
        ReplayComparisonGetter,
    }

    public class ReplayOrderBase
    {
        protected readonly int FirstValue;
        protected readonly int SecondValue;

        public ReplayOrderBase(int first, int second)
        {
            FirstValue = first;
            SecondValue = second;
        }

        public virtual int First => FirstValue;

        public virtual int Second => SecondValue;
    }

    public sealed class ReplayOrderDto : ReplayOrderBase
    {
        private static int _sourceFirstReads;

        public ReplayOrderDto(int first, int second)
            : base(first, second)
        {
            Events.Add("constructor");
            if (Fault == ReplayFault.Constructor && CurrentSource is not null)
            {
                throw Failure();
            }
        }

        public static List<string> Events { get; } = [];

        public static ReplayOrderDto? CurrentSource { get; set; }

        public static ReplayFault Fault { get; private set; }

        public override int First
        {
            get
            {
                var isSource = ReferenceEquals(this, CurrentSource);
                Events.Add(isSource ? "source:first" : "replay:first");
                if (isSource)
                {
                    _sourceFirstReads++;
                }

                if ((Fault == ReplayFault.ArgumentGetter && isSource && _sourceFirstReads == 1) ||
                    (Fault == ReplayFault.SourceComparisonGetter && isSource && _sourceFirstReads == 2) ||
                    (Fault == ReplayFault.ReplayComparisonGetter && !isSource))
                {
                    throw Failure();
                }

                return FirstValue;
            }
        }

        public override int Second
        {
            get
            {
                Events.Add(ReferenceEquals(this, CurrentSource) ? "source:second" : "replay:second");
                return SecondValue;
            }
        }

        public static void Reset(ReplayFault fault)
        {
            Events.Clear();
            _sourceFirstReads = 0;
            Fault = fault;
        }

        private static InvalidOperationException Failure() =>
            new($"replay {Fault} failed");
    }
}
