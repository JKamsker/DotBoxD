namespace DotBoxD.Plugins.Runtime;

internal readonly struct CachedPipelineFanout
{
    private static readonly object[] EmptyPipelines = Array.Empty<object>();
    private readonly State? _state;

    private CachedPipelineFanout(object[] pipelines)
        => _state = new State(pipelines);

    public int Count => Pipelines.Length;

    public object this[int index] => Pipelines[index];

    public static CachedPipelineFanout Empty => default;

    public static CachedPipelineFanout From(List<object>? pipelines)
        => pipelines is null || pipelines.Count == 0
            ? Empty
            : new CachedPipelineFanout(pipelines.ToArray());

    public Enumerator GetEnumerator() => new(Pipelines);

    internal object? ReadResultRegistrationCache()
        => _state is null ? null : Volatile.Read(ref _state.ResultRegistrationCache);

    internal object? CompareExchangeResultRegistrationCache(object value, object? comparand)
    {
        var state = _state ?? throw new InvalidOperationException("An empty fanout cannot cache registrations.");
        return Interlocked.CompareExchange(ref state.ResultRegistrationCache, value, comparand);
    }

    internal CachedPipelineFanout CopyWithoutResultRegistrationCache()
        => _state is null
            ? Empty
            : new CachedPipelineFanout(_state.Pipelines);

    private object[] Pipelines => _state?.Pipelines ?? EmptyPipelines;

    private sealed class State(object[] pipelines)
    {
        // The extra owner is created once with a non-empty fanout. Subscription dispatch uses only Pipelines;
        // multi-pipeline result hooks additionally publish their lazily built aggregate through the cache field.
        public readonly object[] Pipelines = pipelines;
        public object? ResultRegistrationCache;
    }

    internal struct Enumerator
    {
        private readonly object[] _pipelines;
        private int _index;

        public Enumerator(object[] pipelines)
        {
            _pipelines = pipelines;
            _index = -1;
        }

        public object Current => _pipelines[_index];

        public bool MoveNext()
        {
            var next = _index + 1;
            if (next >= _pipelines.Length)
            {
                return false;
            }

            _index = next;
            return true;
        }
    }
}
