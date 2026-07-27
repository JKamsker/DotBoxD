using System.Runtime.ExceptionServices;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff.ValueShapeHandoffProbeSupport;

namespace DotBoxD.Kernels.Benchmarks.Runtime.ValueShapeHandoff;

/// <summary>Keeps one incremental-result publisher alive for every production registry slot.</summary>
internal sealed class ValueShapePublisherPopulation : IDisposable
{
    private const int PublisherCount = 16;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CountdownEvent _ready = new(PublisherCount);
    private readonly ManualResetEventSlim _release = new();
    private readonly Thread[] _threads = new Thread[PublisherCount];
    private Exception? _error;

    private ValueShapePublisherPopulation()
    {
    }

    public static ValueShapePublisherPopulation Start()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var population = new ValueShapePublisherPopulation();
        population.StartThreads();
        return population;
    }

    public void Dispose()
    {
        _release.Set();
        foreach (var thread in _threads)
        {
            if (!thread.Join(Timeout))
            {
                throw new TimeoutException("A shape-cache publisher did not exit.");
            }
        }

        _ready.Dispose();
        _release.Dispose();
        ThrowIfFailed();
    }

    private void StartThreads()
    {
        for (var index = 0; index < _threads.Length; index++)
        {
            var publisherIndex = index;
            var thread = new Thread(() => PublishAndWait(publisherIndex))
            {
                IsBackground = true,
                Name = $"ValueShapeCache publisher {index}",
            };
            _threads[index] = thread;
            thread.Start();
        }

        if (!_ready.Wait(Timeout))
        {
            _release.Set();
            throw new TimeoutException("Shape-cache publishers did not become ready.");
        }

        ThrowIfFailed();
    }

    private void PublishAndWait(int index)
    {
        try
        {
            using var context = CreateContext(maxListLength: CollectionSize + 1, maxMapEntries: 0);
            var source = CreateListSource(context);
            var value = CompiledRuntime.ListAdd(context, source, SandboxValue.FromInt32(index));
            _ready.Signal();
            _release.Wait();
            GC.KeepAlive(value);
        }
        catch (Exception error)
        {
            Interlocked.CompareExchange(ref _error, error, comparand: null);
            _ready.Signal();
            _release.Set();
        }
    }

    private void ThrowIfFailed()
    {
        if (_error is not null)
        {
            ExceptionDispatchInfo.Capture(_error).Throw();
        }
    }
}
