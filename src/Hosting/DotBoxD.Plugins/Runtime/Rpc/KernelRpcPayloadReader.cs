namespace DotBoxD.Plugins;

using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;

/// <summary>
/// Low-level reader used by generated plugin code to decode a known <see cref="KernelRpcValue"/> payload shape
/// without first materializing a full <see cref="KernelRpcValue"/> tree.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public ref struct KernelRpcPayloadReader
{
    private const int MaxDecodeDepth = 64;
    private const int MaxDecodeItems = 10_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private ReadOnlySpan<byte> _remaining;
    private int _items;

    public KernelRpcPayloadReader(ReadOnlySpan<byte> payload)
    {
        _remaining = payload;
        _items = 0;
    }

    public void ReadUnit() => ReadKind(KernelRpcValueKind.Unit);

    public bool ReadBool()
    {
        ReadKind(KernelRpcValueKind.Bool);
        return ReadBoolPayload();
    }

    public int ReadInt32()
    {
        ReadKind(KernelRpcValueKind.I32);
        return BinaryPrimitives.ReadInt32LittleEndian(Read(sizeof(int)));
    }

    public long ReadInt64()
    {
        ReadKind(KernelRpcValueKind.I64);
        return BinaryPrimitives.ReadInt64LittleEndian(Read(sizeof(long)));
    }

    public double ReadDouble()
    {
        ReadKind(KernelRpcValueKind.F64);
        return ReadDoublePayload();
    }

    public string ReadString()
    {
        ReadKind(KernelRpcValueKind.String);
        var length = ReadLength();
        try
        {
            return StrictUtf8.GetString(Read(length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException("Server extension payload contains invalid UTF-8.", ex);
        }
    }

    public Guid ReadGuid()
    {
        ReadKind(KernelRpcValueKind.Guid);
        return new Guid(Read(16));
    }

    public int ReadListHeader()
    {
        ReadKind(KernelRpcValueKind.List);
        return ReadCount();
    }

    public int ReadRecordHeader()
    {
        ReadKind(KernelRpcValueKind.Record);
        return ReadCount();
    }

    public int ReadMapHeader()
    {
        ReadKind(KernelRpcValueKind.Map);
        var count = ReadCount();
        if ((count & 1) != 0)
        {
            throw new FormatException("Server extension map payload has an odd key/value entry count.");
        }

        return count;
    }

    /// <summary>
    /// Consumes one structurally valid wire value without materializing it. Generated clients use this as an
    /// allocation-free validation pass before invoking user-defined DTO constructors during typed projection.
    /// </summary>
    public void SkipValue() => SkipValue(depth: 0);

    public void EnsureConsumed()
    {
        if (!_remaining.IsEmpty)
        {
            throw new FormatException("Server extension payload contains trailing bytes.");
        }
    }

    private int ReadCount()
    {
        var count = ReadLength();
        if (count < 0 || _items > MaxDecodeItems - count)
        {
            throw new FormatException("Server extension payload contains too many items.");
        }

        _items += count;
        return count;
    }

    private void SkipValue(int depth)
    {
        var kind = (KernelRpcValueKind)ReadByte();
        if (TrySkipScalarValue(kind))
        {
            return;
        }

        switch (kind)
        {
            case KernelRpcValueKind.List:
            case KernelRpcValueKind.Record:
            case KernelRpcValueKind.Map:
                SkipItems(kind, depth);
                return;
            default:
                throw new FormatException($"Server extension payload contains unknown value kind '{kind}'.");
        }
    }

    private bool TrySkipScalarValue(KernelRpcValueKind kind)
    {
        switch (kind)
        {
            case KernelRpcValueKind.Unit:
                return true;
            case KernelRpcValueKind.Bool:
                _ = ReadBoolPayload();
                return true;
            case KernelRpcValueKind.I32:
                _ = Read(sizeof(int));
                return true;
            case KernelRpcValueKind.I64:
                _ = Read(sizeof(long));
                return true;
            case KernelRpcValueKind.F64:
                _ = ReadDoublePayload();
                return true;
            case KernelRpcValueKind.String:
                SkipStringPayload();
                return true;
            case KernelRpcValueKind.Guid:
                _ = Read(16);
                return true;
            default:
                return false;
        }
    }

    private void SkipItems(KernelRpcValueKind kind, int depth)
    {
        var nextDepth = depth + 1;
        if (nextDepth > MaxDecodeDepth)
        {
            throw new FormatException("Server extension payload exceeds the maximum nesting depth.");
        }

        var count = ReadCount();
        for (var i = 0; i < count; i++)
        {
            SkipValue(nextDepth);
        }

        if (kind == KernelRpcValueKind.Map && (count & 1) != 0)
        {
            throw new FormatException("Server extension map payload has an odd key/value entry count.");
        }
    }

    private bool ReadBoolPayload()
        => ReadByte() switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("Server extension payload contains an invalid bool value.")
        };

    private double ReadDoublePayload()
    {
        var value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Read(sizeof(long))));
        if (!double.IsFinite(value))
        {
            throw new FormatException("Server extension payload contains a non-finite F64 value.");
        }

        return value;
    }

    private void SkipStringPayload()
    {
        var length = ReadLength();
        try
        {
            _ = StrictUtf8.GetCharCount(Read(length));
        }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException("Server extension payload contains invalid UTF-8.", ex);
        }
    }

    private void ReadKind(KernelRpcValueKind expected)
    {
        var actual = (KernelRpcValueKind)ReadByte();
        if (actual != expected)
        {
            throw new FormatException(
                $"Server extension payload expected '{expected}' but received '{actual}'.");
        }
    }

    private byte ReadByte()
    {
        if (_remaining.IsEmpty)
        {
            throw new FormatException("Server extension payload ended unexpectedly.");
        }

        var value = _remaining[0];
        _remaining = _remaining[1..];
        return value;
    }

    private int ReadLength()
    {
        ulong result = 0;
        var shift = 0;
        while (shift < 35)
        {
            var next = ReadByte();
            result |= (ulong)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                if (result > int.MaxValue)
                {
                    throw new FormatException("Server extension payload contains an invalid length prefix.");
                }

                return (int)result;
            }

            shift += 7;
        }

        throw new FormatException("Server extension payload contains an invalid length prefix.");
    }

    private ReadOnlySpan<byte> Read(int length)
    {
        if (length < 0 || _remaining.Length < length)
        {
            throw new FormatException("Server extension payload ended unexpectedly.");
        }

        var bytes = _remaining[..length];
        _remaining = _remaining[length..];
        return bytes;
    }
}
