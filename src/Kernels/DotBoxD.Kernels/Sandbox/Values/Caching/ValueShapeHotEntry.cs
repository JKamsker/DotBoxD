namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>One producer thread's weak, generation-checked shape publication.</summary>
internal sealed class ValueShapeHotEntry
{
    private readonly WeakReference<SandboxValue> _localValue;
    private readonly WeakReference<SandboxValue> _publishedValue;
    private ShapeInfo _localInfo;
    private ShapeInfo _publishedInfo;
    private ValueShapeGeneration _localGeneration;
    private ValueShapeGeneration _publishedGeneration;
    private bool _isCrossThreadPublished;
    private bool _hasPublishedValue;
    private bool _registrationAttempted;
    private int _registrySlot = -1;
    private int _directoryPublicationRequested;
    private long _publicationVersion;

    public ValueShapeHotEntry(
        SandboxValue value,
        ValueShapeGeneration generation,
        ShapeInfo info)
    {
        _localValue = new WeakReference<SandboxValue>(value);
        _publishedValue = new WeakReference<SandboxValue>(value);
        _localGeneration = generation;
        _publishedGeneration = generation;
        _localInfo = info;
        _publishedInfo = info;
    }

    /// <summary>The owning thread is the only writer, so its local read needs no synchronization.</summary>
    public bool TryGetLocal(
        SandboxValue value,
        ValueShapeGeneration generation,
        out ShapeInfo info)
    {
        if (TryGet(
                _localValue,
                _localGeneration,
                _localInfo,
                value,
                generation,
                out info))
        {
            return true;
        }

        if (_hasPublishedValue)
        {
            return TryGet(
                _publishedValue,
                _publishedGeneration,
                _publishedInfo,
                value,
                generation,
                out info);
        }

        info = default;
        return false;
    }

    public bool TryGetPublished(
        SandboxValue value,
        ValueShapeGeneration generation,
        out ShapeInfo info)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var version = Volatile.Read(ref _publicationVersion);
            if ((version & 1) != 0)
            {
                Thread.SpinWait(1 << attempt);
                continue;
            }

            var found = TryGet(
                _publishedValue,
                _publishedGeneration,
                _publishedInfo,
                value,
                generation,
                out info);
            Thread.MemoryBarrier();
            if (version == Volatile.Read(ref _publicationVersion))
            {
                return found;
            }
        }

        info = default;
        return false;
    }

    public void SetLocal(
        SandboxValue value,
        ValueShapeGeneration generation,
        ShapeInfo info)
    {
        _localInfo = info;
        _localGeneration = generation;
        _localValue.SetTarget(value);
    }

    public void Publish(
        SandboxValue value,
        ValueShapeGeneration generation,
        ShapeInfo info)
    {
        _hasPublishedValue = true;
        if (_isCrossThreadPublished)
        {
            var writeVersion = Interlocked.Increment(ref _publicationVersion);
            SetPublished(value, generation, info);
            Volatile.Write(ref _publicationVersion, unchecked(writeVersion + 1));
            return;
        }

        SetPublished(value, generation, info);
    }

    public void EnableCrossThreadPublication(int registrySlot = -1)
    {
        _registrySlot = registrySlot;
        _isCrossThreadPublished = true;
    }

    public int RegistrySlot => _registrySlot;

    public bool IsDirectoryPublicationRequested =>
        Volatile.Read(ref _directoryPublicationRequested) != 0;

    public void RequestDirectoryPublication() =>
        Volatile.Write(ref _directoryPublicationRequested, 1);

    public bool TryBeginRegistration()
    {
        if (_registrationAttempted)
        {
            return false;
        }

        _registrationAttempted = true;
        return true;
    }

    private static bool TryGet(
        WeakReference<SandboxValue> weakValue,
        ValueShapeGeneration cachedGeneration,
        ShapeInfo cachedInfo,
        SandboxValue value,
        ValueShapeGeneration generation,
        out ShapeInfo info)
    {
        if (cachedGeneration == generation &&
            weakValue.TryGetTarget(out var cached) &&
            ReferenceEquals(cached, value))
        {
            info = cachedInfo;
            return true;
        }

        info = default;
        return false;
    }

    private void SetPublished(
        SandboxValue value,
        ValueShapeGeneration generation,
        ShapeInfo info)
    {
        _publishedInfo = info;
        _publishedGeneration = generation;
        _publishedValue.SetTarget(value);
    }
}
