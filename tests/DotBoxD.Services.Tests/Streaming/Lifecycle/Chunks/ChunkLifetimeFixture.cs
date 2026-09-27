using System.Runtime.CompilerServices;
using DotBoxD.Services.Streaming.Frames;
using Xunit;

namespace DotBoxD.Services.Tests.Streaming.Lifecycle.Chunks;

internal sealed class ChunkLifetimeFixture(object[] retained, WeakReference[] references) : IDisposable
{
    public WeakReference[] References { get; } = references;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ChunkLifetimeFixture Create(int count, bool dispose, bool credit, bool retainRead = false, bool retain = true)
    {
        var retained = new List<object>();
        var references = new List<WeakReference>();
        for (var index = 0; index < count; index++)
        {
            var fixture = ChunkDisposalFixture.Create(pendingRead: retainRead);
            references.AddRange(fixture.OwnershipReferences());
            fixture.Manager.CompleteInbound(fixture.Receiver.Handle.StreamId);
            if (dispose)
            {
                if (credit)
                {
                    fixture.Chunk.Dispose();
                }
                else
                {
                    fixture.Chunk.DisposeWithoutCredit();
                }
            }
            if (retain)
            {
                retained.Add(retainRead ? fixture.Read! : fixture.Chunk);
            }
        }
        return new ChunkLifetimeFixture(retained.ToArray(), references.ToArray());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ChunkLifetimeFixture CreateWithPendingCredit(Task pendingCredit)
    {
        var fixture = ChunkDisposalFixture.Create(pendingCredit: pendingCredit);
        fixture.Chunk.Dispose();
        Assert.Equal(1, fixture.Sender.Credits);
        return new ChunkLifetimeFixture([fixture.Chunk], fixture.OwnershipReferences());
    }

    public static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    public async Task AssertCollectedAsync()
    {
        var deadline = Environment.TickCount64 + 5_000;
        do
        {
            Collect();
            if (References.All(reference => !reference.IsAlive))
            {
                return;
            }
            await Task.Delay(20);
        } while (Environment.TickCount64 < deadline);

        Assert.All(References, reference => Assert.False(reference.IsAlive));
    }

    public void Dispose()
    {
        foreach (var item in retained)
        {
            var chunk = item as RpcStreamChunk ?? ((Task<RpcStreamChunk?>)item).GetAwaiter().GetResult();
            chunk!.DisposeWithoutCredit();
        }
    }
}
