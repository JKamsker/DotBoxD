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
    /// Creates the legacy contractless serializer preset used by some Unity projects.
    /// ContractlessStandardResolver may use reflection and is not an IL2CPP/AOT guarantee;
    /// AOT applications should call <see cref="CreateWithResolver"/> with generated formatters.
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
        new(CreateOptions(resolver ?? throw new ArgumentNullException(nameof(resolver))));

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
        var effectiveResolvers = new IFormatterResolver[extraCount + 4];
        for (var i = 0; i < extraCount; i++)
        {
            // Reject null elements eagerly: a null slipped into CompositeResolver.Create otherwise
            // fails opaquely on the first Serialize/Deserialize, far from the configuration mistake.
            effectiveResolvers[i] = resolvers[i]
                ?? throw new ArgumentException("Resolvers must not contain null elements.", nameof(resolvers));
        }

        effectiveResolvers[extraCount] = WellFormedStringResolver.Instance;
        effectiveResolvers[extraCount + 1] = NativeDateTimeResolver.Instance;
        effectiveResolvers[extraCount + 2] = StandardResolver.Instance;
        effectiveResolvers[extraCount + 3] = ContractlessStandardResolver.Instance;

        return MessagePackSerializerOptions.Standard
            .WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[]
                {
                    RpcRequestFormatter.Instance,
                    RpcResponseFormatter.Instance,
                    RpcStreamHandleFormatter.Instance,
                    ReadOnlyMemoryByteFormatter.Instance,
                    RpcObjectFormatter.Instance,
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
        if (writer is null)
        {
            throw new ArgumentNullException(nameof(writer));
        }

        ThrowIfUnsupportedObjectDeclaredScalar(typeof(T), value);

        try
        {
            if (ConstructorReplayGuard.TrySerialize(writer, value, _options))
            {
                return;
            }

            MessagePackSerializer.Serialize(writer, value, _options);
        }
        catch (MessagePackSerializationException ex)
        {
            if (TryGetRpcEnvelopeValidationMessage(ex, out var message))
            {
                throw new MessagePackSerializationException(message, ex);
            }

            throw;
        }
    }

    private static void ThrowIfUnsupportedObjectDeclaredScalar<T>(Type declaredType, T value)
    {
        if (declaredType != typeof(object) || value is null)
        {
            return;
        }

        var runtimeType = value.GetType();
        if (runtimeType == typeof(Guid) || runtimeType == typeof(DateTimeOffset))
        {
            throw new MessagePackSerializationException(
                $"{runtimeType.FullName} cannot be serialized through an object-declared payload " +
                "because MessagePack cannot deserialize it back to the same CLR type without a declared target type.");
        }
    }

    public T Deserialize<T>(ReadOnlyMemory<byte> data)
    {
        try
        {
            var value = MessagePackSerializer.Deserialize<T>(data, _options, out var bytesRead, CancellationToken.None);
            ThrowIfTrailingBytes(data.Length, bytesRead);
            return value;
        }
        catch (MessagePackSerializationException ex)
        {
            if (TryGetRpcEnvelopeValidationMessage(ex, out var message))
            {
                throw new MessagePackSerializationException(message, ex);
            }

            throw;
        }
    }

    public object? Deserialize(ReadOnlyMemory<byte> data, Type type)
    {
        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        try
        {
            var reader = new MessagePackReader(data);
            var value = MessagePackSerializer.Deserialize(type, ref reader, _options);
            ThrowIfTrailingBytes(data.Length, checked((int)reader.Consumed));
            return value;
        }
        catch (MessagePackSerializationException ex)
        {
            if (TryGetRpcEnvelopeValidationMessage(ex, out var message))
            {
                throw new MessagePackSerializationException(message, ex);
            }

            throw;
        }
    }

    private static void ThrowIfTrailingBytes(int totalLength, int bytesRead)
    {
        if (bytesRead != totalLength)
        {
            throw new MessagePackSerializationException("Trailing bytes after serialized value.");
        }
    }

    private static bool TryGetRpcEnvelopeValidationMessage(
        MessagePackSerializationException exception,
        out string message)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is RpcEnvelopeValidationException validationException)
            {
                message = validationException.Message;
                return true;
            }
        }

        message = string.Empty;
        return false;
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

internal static class RpcEnvelopeStringValidation
{
    public static void ThrowIfMalformedUtf16(string? value, string envelopeName, string fieldName)
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

                throw MalformedUtf16(envelopeName, fieldName);
            }

            if (char.IsLowSurrogate(current))
            {
                throw MalformedUtf16(envelopeName, fieldName);
            }
        }
    }

    private static RpcEnvelopeValidationException MalformedUtf16(string envelopeName, string fieldName)
        => new(
            $"RPC {envelopeName} {fieldName} contains malformed UTF-16 text with an unpaired surrogate.");
}

internal sealed class RpcEnvelopeValidationException(string message) : MessagePackSerializationException(message)
{
}
