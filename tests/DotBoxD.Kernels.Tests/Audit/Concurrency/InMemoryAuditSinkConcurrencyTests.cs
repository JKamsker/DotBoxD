using System.Collections.Concurrent;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class InMemoryAuditSinkConcurrencyTests
{
    [Fact]
    public async Task Concurrent_writes_preserve_snapshot_count_and_sequence_numbers()
    {
        var failure = await FindFirstConcurrencyInvariantFailureAsync();

        Assert.True(failure is null, failure);
    }

    private static async Task<string?> FindFirstConcurrencyInvariantFailureAsync()
    {
        const int attempts = 3;
        const int writerCount = 12;
        const int eventsPerWriter = 10_000;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var failure = await RunAttemptAsync(attempt, writerCount, eventsPerWriter);
            if (failure is not null)
            {
                return failure;
            }
        }

        return null;
    }

    private static async Task<string?> RunAttemptAsync(int attempt, int writerCount, int eventsPerWriter)
    {
        var expected = writerCount * eventsPerWriter;
        var sink = new InMemoryAuditSink();
        var runId = SandboxRunId.New();
        var start = new ManualResetEventSlim();
        using var snapshotCancellation = new CancellationTokenSource();
        var snapshotFailures = new ConcurrentQueue<Exception>();

        var snapshotReader = Task.Run(() =>
        {
            var snapshotsRead = 0;
            while (!snapshotCancellation.IsCancellationRequested)
            {
                try
                {
                    _ = sink.Events.Count;
                    snapshotsRead++;
                    if (snapshotsRead % 32 == 0)
                    {
                        Thread.Sleep(1);
                    }
                    else
                    {
                        Thread.Yield();
                    }
                }
                catch (Exception ex)
                {
                    snapshotFailures.Enqueue(ex);
                }
            }
        });

        var writers = Enumerable.Range(0, writerCount)
            .Select(writer => Task.Run(() =>
            {
                start.Wait();

                for (var index = 0; index < eventsPerWriter; index++)
                {
                    sink.Write(EventFor(runId, writer, index));
                }
            }))
            .ToArray();

        start.Set();

        try
        {
            await Task.WhenAll(writers);
        }
        finally
        {
            snapshotCancellation.Cancel();
            await snapshotReader;
        }

        if (snapshotFailures.TryPeek(out var snapshotFailure))
        {
            return $"Attempt {attempt}: concurrent Events snapshot threw {snapshotFailure.GetType().Name}: {snapshotFailure.Message}";
        }

        var events = sink.Events;
        var nullEvents = events.Count(e => e is null);
        if (nullEvents != 0)
        {
            return $"Attempt {attempt}: Events snapshot contained {nullEvents} null entries.";
        }

        var sequences = events.Select(e => e.SequenceNumber).ToArray();
        var uniqueSequences = sequences.Distinct().Count();

        if (events.Count != expected ||
            sink.EventsWritten != expected ||
            uniqueSequences != expected ||
            sequences.Length == 0 ||
            sequences.Min() != 1 ||
            sequences.Max() != expected)
        {
            return
                $"Attempt {attempt}: count={events.Count}, eventsWritten={sink.EventsWritten}, " +
                $"uniqueSequences={uniqueSequences}, minSequence={(sequences.Length == 0 ? 0 : sequences.Min())}, " +
                $"maxSequence={(sequences.Length == 0 ? 0 : sequences.Max())}, expected={expected}.";
        }

        return null;
    }

    private static SandboxAuditEvent EventFor(SandboxRunId runId, int writer, int index)
        => new(
            runId,
            "BindingCall",
            DateTimeOffset.UnixEpoch,
            Success: true,
            BindingId: "test.concurrent",
            ResourceId: $"writer:{writer}:{index}");
}
