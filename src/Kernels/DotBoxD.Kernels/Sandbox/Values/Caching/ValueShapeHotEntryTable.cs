using System.Runtime.CompilerServices;

namespace DotBoxD.Kernels.Sandbox.Values;

/// <summary>
/// A bounded weak registry of producer entries, addressed through a fixed identity-hash directory.
/// Hash collisions are misses because the entry still verifies exact identity and generation.
/// </summary>
internal sealed class ValueShapeHotEntryTable
{
    private readonly object _gate = new();
    private readonly long[] _directory;
    private readonly WeakReference<ValueShapeHotEntry>?[] _entries;

    public ValueShapeHotEntryTable(int capacity, int directoryCapacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (directoryCapacity <= 0 || (directoryCapacity & (directoryCapacity - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(directoryCapacity));
        }

        _entries = new WeakReference<ValueShapeHotEntry>[capacity];
        _directory = new long[directoryCapacity];
    }

    public bool TryRegister(ValueShapeHotEntry entry)
    {
        lock (_gate)
        {
            for (var index = 0; index < _entries.Length; index++)
            {
                var weakEntry = _entries[index];
                if (weakEntry is not null && weakEntry.TryGetTarget(out _))
                {
                    continue;
                }

                entry.EnableCrossThreadPublication(index);
                Volatile.Write(ref _entries[index], new WeakReference<ValueShapeHotEntry>(entry));
                return true;
            }
        }

        return false;
    }

    public void Publish(ValueShapeHotEntry entry, SandboxValue value)
        => Publish(entry, value, RuntimeHelpers.GetHashCode(value));

    public void PublishIfRequested(ValueShapeHotEntry entry, SandboxValue value)
    {
        if (entry.IsDirectoryPublicationRequested)
        {
            Publish(entry, value);
        }
    }

    public bool TryGet(
        SandboxValue value,
        ValueShapeGeneration generation,
        ValueShapeHotEntry? localEntry,
        out ShapeInfo info)
    {
        var identityHash = RuntimeHelpers.GetHashCode(value);
        if (TryGetFromDirectory(value, generation, localEntry, identityHash, out info))
        {
            return true;
        }

        for (var slot = 0; slot < _entries.Length; slot++)
        {
            if (TryGetFromSlot(slot, value, generation, localEntry, out info, out var entry))
            {
                entry.RequestDirectoryPublication();
                Publish(entry, value, identityHash);
                return true;
            }
        }

        info = default;
        return false;
    }

    private bool TryGetFromDirectory(
        SandboxValue value,
        ValueShapeGeneration generation,
        ValueShapeHotEntry? localEntry,
        int identityHash,
        out ShapeInfo info)
    {
        var directoryIndex = identityHash & (_directory.Length - 1);
        var token = Volatile.Read(ref _directory[directoryIndex]);
        if ((uint)(token >> 32) != (uint)identityHash)
        {
            info = default;
            return false;
        }

        var slot = (int)(uint)token - 1;
        return TryGetFromSlot(slot, value, generation, localEntry, out info, out _);
    }

    private bool TryGetFromSlot(
        int slot,
        SandboxValue value,
        ValueShapeGeneration generation,
        ValueShapeHotEntry? localEntry,
        out ShapeInfo info,
        out ValueShapeHotEntry entry)
    {
        if ((uint)slot >= (uint)_entries.Length)
        {
            info = default;
            entry = null!;
            return false;
        }

        var weakEntry = Volatile.Read(ref _entries[slot]);
        if (weakEntry is not null &&
            weakEntry.TryGetTarget(out var target) &&
            !ReferenceEquals(target, localEntry) &&
            target.TryGetPublished(value, generation, out info))
        {
            entry = target;
            return true;
        }

        info = default;
        entry = null!;
        return false;
    }

    private void Publish(ValueShapeHotEntry entry, SandboxValue value, int identityHash)
    {
        var slot = entry.RegistrySlot;
        if ((uint)slot >= (uint)_entries.Length)
        {
            return;
        }

        var directoryIndex = identityHash & (_directory.Length - 1);
        var token = ((long)(uint)identityHash << 32) | (uint)(slot + 1);
        Volatile.Write(ref _directory[directoryIndex], token);
    }
}
