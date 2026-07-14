using System.Collections.ObjectModel;

namespace DotBoxD.Plugins.Debugging;

internal sealed class PluginDebugAssemblyStore(int maxBytes)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MemoryStream> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _completed = new(StringComparer.Ordinal);
    private int _totalBytes;

    public int Append(string fileName, int offset, ReadOnlySpan<byte> chunk, bool complete)
    {
        ValidateFileName(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (chunk.IsEmpty && !complete)
        {
            throw new ArgumentException("A non-final assembly chunk cannot be empty.", nameof(chunk));
        }

        lock (_gate)
        {
            if (_completed.ContainsKey(fileName))
            {
                throw new ArgumentException($"Assembly '{fileName}' has already been uploaded.", nameof(fileName));
            }

            var stream = GetPending(fileName, offset);
            if (stream.Length != offset)
            {
                throw new ArgumentException(
                    $"Assembly chunk offset {offset} does not match the next expected offset {stream.Length}.",
                    nameof(offset));
            }

            if (_totalBytes > maxBytes - chunk.Length)
            {
                throw new ArgumentException($"Assembly uploads exceed the {maxBytes}-byte host limit.", nameof(chunk));
            }

            stream.Write(chunk);
            _totalBytes += chunk.Length;
            var received = checked((int)stream.Length);
            if (complete)
            {
                if (received == 0)
                {
                    throw new ArgumentException("Uploaded assemblies cannot be empty.", nameof(chunk));
                }

                _completed.Add(fileName, stream.ToArray());
                _pending.Remove(fileName);
                stream.Dispose();
            }

            return received;
        }
    }

    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Snapshot()
    {
        lock (_gate)
        {
            var snapshot = _completed.ToDictionary(
                item => item.Key,
                item => (ReadOnlyMemory<byte>)item.Value,
                StringComparer.Ordinal);
            return new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(snapshot);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (var stream in _pending.Values)
            {
                stream.Dispose();
            }

            _pending.Clear();
            _completed.Clear();
            _totalBytes = 0;
        }
    }

    private MemoryStream GetPending(string fileName, int offset)
    {
        if (_pending.TryGetValue(fileName, out var stream))
        {
            return stream;
        }

        if (offset != 0)
        {
            throw new ArgumentException("The first assembly chunk must start at offset zero.", nameof(offset));
        }

        stream = new MemoryStream();
        _pending.Add(fileName, stream);
        return stream;
    }

    private static void ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny('/', '\\') >= 0 ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Assembly names must be leaf .dll file names.", nameof(fileName));
        }
    }
}
