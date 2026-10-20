using DotBoxD.Services.Protocol;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Outbound.Chunking;

public sealed class RpcPipeChunkingTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(65535, false)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    [InlineData(65537, true)]
    [InlineData(131073, false)]
    [InlineData(131073, true)]
    [InlineData(MessageFramer.MaxMessageSize + 1, false)]
    [InlineData(MessageFramer.MaxMessageSize + 1, true)]
    public async Task Single_segment_is_split_into_bounded_ordered_chunks(int length, bool owned)
    {
        await using var fixture = await PipeChunkingFixture.Create([length], owned);

        await fixture.Pump().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(length, fixture.BytesSent);
        Assert.Equal((length + PipeChunkingFixture.ChunkSize - 1) / PipeChunkingFixture.ChunkSize, fixture.SentLengths.Count);
        Assert.All(fixture.SentLengths, size => Assert.InRange(size, 1, PipeChunkingFixture.ChunkSize));
        await fixture.AssertRemaining(length);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Mixed_segments_coalesce_available_bytes_and_preserve_writer_completion(bool owned, bool writerCompleted)
    {
        int[] lengths = [1, 65537, 0, 7, 131072];
        await using var fixture = await PipeChunkingFixture.Create(lengths, owned, writerCompleted);
        var pump = fixture.Pump();
        if (!writerCompleted)
        {
            Assert.False(pump.IsCompleted);
            await fixture.CompleteWriter();
        }

        await pump.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(lengths.Sum(), fixture.BytesSent);
        Assert.Equal(new[] { 65536, 65536, 65536, 9 }, fixture.SentLengths);
        await fixture.AssertRemaining(fixture.Expected.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Short_buffer_is_sent_before_writer_completes(bool owned)
    {
        await using var fixture = await PipeChunkingFixture.Create([1, 7], owned, writerCompleted: false);
        var pump = fixture.Pump();
        try
        {
            Assert.Equal(8, fixture.BytesSent);
            Assert.Equal(new[] { 8 }, fixture.SentLengths);
            Assert.False(pump.IsCompleted);
        }
        finally
        {
            await fixture.CompleteWriter();
            await pump.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(256, 16)]
    [InlineData(1024, 16)]
    [InlineData(4096, 16)]
    [InlineData(16384, 64)]
    [InlineData(65536, 16)]
    public async Task Coalescing_threshold_preserves_large_segment_controls(int segmentSize, int frames)
    {
        const int bytes = 1024 * 1024;
        await using var fixture = await PipeChunkingFixture.Create(Enumerable.Repeat(segmentSize, bytes / segmentSize).ToArray(), owned: false);
        await fixture.Pump().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(bytes, fixture.BytesSent);
        Assert.Equal(frames, fixture.SentLengths.Count);
        await fixture.AssertRemaining(bytes);
    }
}
