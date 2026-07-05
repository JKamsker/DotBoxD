using System.Buffers;
using DotBoxD.Services.Serialization;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

namespace DotBoxD.Codecs.MessagePack;

/// <summary>
/// MessagePack-based serializer implementation.
/// </summary>
public sealed class MessagePackRpcSerializer : ISerializer
{
    private readonly MessagePackSerializerOptions _options;

    /// <summary>
    /// Gets the MessagePack options used for RPC envelopes and method payloads.
    /// </summary>
    public MessagePackSerializerOptions Options => _options;

    /// <summary>
    /// Creates a new MessagePack serializer with default options.
    /// </summary>
    public MessagePackRpcSerializer() : this(CreateDefaultOptions())
    {
    }

    /// <summary>
    /// Creates a new MessagePack serializer with custom options.
    /// </summary>
    public MessagePackRpcSerializer(MessagePackSerializerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Creates a MessagePack serializer optimized for Unity compatibility.
    /// Uses ContractlessStandardResolver which doesn't require attributes.
    /// </summary>
    public static MessagePackRpcSerializer CreateUnityCompatible()
    {
        var options = CreateOptions(ContractlessStandardResolver.Instance);

        return new MessagePackRpcSerializer(options);
    }

    /// <summary>
    /// Creates a serializer using the supplied resolver plus DotBoxD's binary payload formatters.
    /// </summary>
    public static MessagePackRpcSerializer CreateWithResolver(IFormatterResolver resolver) =>
        new(CreateOptions(resolver));

    /// <summary>
    /// Creates MessagePack options that include DotBoxD's payload formatters before user resolvers.
    /// </summary>
    public static MessagePackSerializerOptions CreateOptions(params IFormatterResolver[] resolvers)
    {
        // A null array (CreateOptions(null)) is legal C# for a params parameter and would otherwise be
        // treated as "no resolvers", silently dropping all the caller's custom formatters — the same
        // silent failure the null-element guard below prevents. Reject it eagerly. CreateOptions() with
        // no arguments still receives an empty (non-null) array and is unaffected.
        if (resolvers is null)
        {
            throw new ArgumentNullException(nameof(resolvers));
        }

        var extraCount = resolvers.Length;
        var effectiveResolvers = new IFormatterResolver[extraCount + 3];
        for (var i = 0; i < extraCount; i++)
        {
            // Reject null elements eagerly: a null slipped into CompositeResolver.Create otherwise
            // fails opaquely on the first Serialize/Deserialize, far from the configuration mistake.
            effectiveResolvers[i] = resolvers[i]
                ?? throw new ArgumentException("Resolvers must not contain null elements.", nameof(resolvers));
        }

        effectiveResolvers[extraCount] = WellFormedStringResolver.Instance;
        effectiveResolvers[extraCount + 1] = StandardResolver.Instance;
        effectiveResolvers[extraCount + 2] = ContractlessStandardResolver.Instance;

        return MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[]
                {
                    RpcRequestFormatter.Instance,
                    RpcResponseFormatter.Instance,
                    ReadOnlyMemoryByteFormatter.Instance,
                },
                effectiveResolvers))
            .WithSecurity(MessagePackSecurity.UntrustedData);
    }

    private static MessagePackSerializerOptions CreateDefaultOptions()
    {
        return CreateOptions();
    }

    public void Serialize<T>(System.Buffers.IBufferWriter<byte> writer, T value)
    {
        MessagePackSerializer.Serialize(writer, value, _options);
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        var value = MessagePackSerializer.Deserialize<T>(data, _options, out var bytesRead, CancellationToken.None);
        ThrowIfTrailingBytes(data.Length, bytesRead);
        return value;
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        var reader = new MessagePackReader(data);
        var value = MessagePackSerializer.Deserialize(type, ref reader, _options);
        ThrowIfTrailingBytes(data.Length, checked((int)reader.Consumed));
        return value;
    }

    private static void ThrowIfTrailingBytes(int totalLength, int bytesRead)
    {
        if (bytesRead != totalLength)
        {
            throw new MessagePackSerializationException("Trailing bytes after serialized value.");
        }
    }

    private sealed class WellFormedStringResolver : IFormatterResolver
    {
        public static readonly WellFormedStringResolver Instance = new();

        public IMessagePackFormatter<T>? GetFormatter<T>() =>
            typeof(T) == typeof(string)
                ? (IMessagePackFormatter<T>)(object)WellFormedStringFormatter.Instance
                : null;
    }

    internal sealed class WellFormedStringFormatter : IMessagePackFormatter<string?>
    {
        public static readonly WellFormedStringFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            string? value,
            MessagePackSerializerOptions options)
        {
            RequireWellFormedUtf16(value);
            writer.Write(value);
        }

        public string? Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options) =>
            reader.ReadString();

        private static void RequireWellFormedUtf16(string? value)
        {
            if (value is null)
            {
                return;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];
                if (char.IsHighSurrogate(current))
                {
                    if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        i++;
                        continue;
                    }

                    throw MalformedUtf16();
                }

                if (char.IsLowSurrogate(current))
                {
                    throw MalformedUtf16();
                }
            }
        }

        private static MessagePackSerializationException MalformedUtf16() =>
            new("String payload contains malformed UTF-16 text with an unpaired surrogate.");
    }

    internal sealed class ReadOnlyMemoryByteFormatter : IMessagePackFormatter<ReadOnlyMemory<byte>>
    {
        public static readonly ReadOnlyMemoryByteFormatter Instance = new();

        public void Serialize(
            ref MessagePackWriter writer,
            ReadOnlyMemory<byte> value,
            MessagePackSerializerOptions options) =>
            writer.Write(value.Span);

        public ReadOnlyMemory<byte> Deserialize(
            ref MessagePackReader reader,
            MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            var bytes = reader.ReadBytes();
            return bytes is { } sequence
                ? sequence.ToArray()
                : ReadOnlyMemory<byte>.Empty;
        }
    }
}
