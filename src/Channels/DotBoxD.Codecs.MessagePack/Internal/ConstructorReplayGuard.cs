using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class ConstructorReplayGuard
{
    private static readonly ConcurrentDictionary<Type, ConstructorReplayGuard> Guards = new();
    private static readonly ConstructorReplayGuard None = new(null, [], [], [], useSerializedReplay: false);

    private readonly ConstructorInfo? _constructor;
    private readonly ParameterInfo[] _parameters;
    private readonly PropertyInfo[] _parameterProperties;
    private readonly PropertyInfo[] _boundProperties;
    private readonly bool _useSerializedReplay;

    private ConstructorReplayGuard(
        ConstructorInfo? constructor,
        ParameterInfo[] parameters,
        PropertyInfo[] parameterProperties,
        PropertyInfo[] boundProperties,
        bool useSerializedReplay)
    {
        _constructor = constructor;
        _parameters = parameters;
        _parameterProperties = parameterProperties;
        _boundProperties = boundProperties;
        _useSerializedReplay = useSerializedReplay;
    }

    public static bool TrySerialize<T>(
        IBufferWriter<byte> writer,
        T value,
        MessagePackSerializerOptions options)
    {
        if (value is null)
        {
            return false;
        }

        var declaredType = typeof(T);
        if (declaredType.IsValueType || declaredType == typeof(string))
        {
            return false;
        }

        var runtimeType = declaredType.IsSealed ? declaredType : value.GetType();
        if (runtimeType.IsValueType || runtimeType == typeof(string))
        {
            return false;
        }

        var guard = runtimeType == declaredType
            ? GetOrAddDeclaredTypeGuard<T>(declaredType)
            : Guards.GetOrAdd(runtimeType, Create);
        if (ReferenceEquals(guard, None))
        {
            return false;
        }

        if (!guard._useSerializedReplay)
        {
            guard.ThrowIfConstructorReplayChangesValues(value);
            return false;
        }

        guard.SerializeWithReplayValidation(writer, value, options);
        return true;
    }

    private static ConstructorReplayGuard GetOrAddDeclaredTypeGuard<T>(Type declaredType)
    {
        var cached = Volatile.Read(ref DeclaredTypeGuardCache<T>.Guard);
        if (cached is not null)
        {
            return cached;
        }

        var resolved = Guards.GetOrAdd(declaredType, Create);
        return Interlocked.CompareExchange(
            ref DeclaredTypeGuardCache<T>.Guard,
            resolved,
            null) ?? resolved;
    }

    private static ConstructorReplayGuard Create(Type type)
    {
        if (!type.IsClass || type == typeof(string))
        {
            return None;
        }

        var getOnlyProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.GetMethod is { IsStatic: false } &&
                property.SetMethod is null &&
                property.GetIndexParameters().Length == 0)
            .ToArray();
        if (getOnlyProperties.Length == 0)
        {
            return None;
        }

        var readableProperties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property =>
                property.GetMethod is { IsStatic: false } &&
                property.GetIndexParameters().Length == 0)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);
        var constructor = type.GetConstructors()
            .Select(constructor => new
            {
                Constructor = constructor,
                Parameters = constructor.GetParameters(),
            })
            .Where(candidate => candidate.Parameters.Length > 0)
            .Where(candidate => candidate.Parameters.All(parameter =>
                TryGetCompatibleProperty(readableProperties, parameter, out _)))
            .OrderByDescending(static candidate => candidate.Parameters.Length)
            .FirstOrDefault();
        if (constructor is null)
        {
            return None;
        }

        var boundProperties = constructor.Parameters
            .Select(parameter => readableProperties[parameter.Name!])
            .Where(property => getOnlyProperties.Contains(property))
            .ToArray();
        if (boundProperties.Length == 0)
        {
            return None;
        }

        return new ConstructorReplayGuard(
            constructor.Constructor,
            constructor.Parameters,
            constructor.Parameters.Select(parameter => readableProperties[parameter.Name!]).ToArray(),
            boundProperties,
            boundProperties.Any(static property => !IsSimpleComparableType(property.PropertyType)));
    }

    private static bool TryGetCompatibleProperty(
        IReadOnlyDictionary<string, PropertyInfo> properties,
        ParameterInfo parameter,
        out PropertyInfo property)
    {
        property = null!;
        if (parameter.Name is null ||
            !properties.TryGetValue(parameter.Name, out property))
        {
            return false;
        }

        return parameter.ParameterType.IsAssignableFrom(property.PropertyType);
    }

    private void ThrowIfConstructorReplayChangesValues<T>(T value)
    {
        try
        {
            var replayed = InvokeConstructor(value);
            for (var i = 0; i < _boundProperties.Length; i++)
            {
                var property = _boundProperties[i];
                if (!Equals(property.GetValue(value), property.GetValue(replayed)))
                {
                    ThrowChangingValues(value!.GetType());
                }
            }
        }
        catch (TargetInvocationException ex)
        {
            ThrowChangingValues(value!.GetType(), ex.InnerException ?? ex);
            throw;
        }
    }

    private object InvokeConstructor<T>(T value)
    {
        var arguments = new object?[_parameters.Length];
        for (var i = 0; i < _parameters.Length; i++)
        {
            arguments[i] = _parameterProperties[i].GetValue(value);
        }

        return _constructor!.Invoke(arguments);
    }

    private void SerializeWithReplayValidation<T>(
        IBufferWriter<byte> writer,
        T value,
        MessagePackSerializerOptions options)
    {
        var serialized = new ArrayBufferWriter<byte>();
        MessagePackSerializer.Serialize(serialized, value, options);

        var reader = new MessagePackReader(serialized.WrittenMemory);
        var replayed = MessagePackSerializer.Deserialize(value!.GetType(), ref reader, options);
        ThrowIfTrailingBytes(serialized.WrittenCount, checked((int)reader.Consumed));

        var replayedSerialized = new ArrayBufferWriter<byte>();
        MessagePackSerializer.Serialize(value.GetType(), replayedSerialized, replayed, options, CancellationToken.None);
        if (!serialized.WrittenSpan.SequenceEqual(replayedSerialized.WrittenSpan))
        {
            ThrowChangingValues(value.GetType());
        }

        writer.Write(serialized.WrittenSpan);
    }

    private static bool IsSimpleComparableType(Type type) =>
        type.IsPrimitive ||
        type.IsEnum ||
        type == typeof(string) ||
        type == typeof(decimal) ||
        type == typeof(Guid) ||
        type == typeof(DateTime) ||
        type == typeof(DateTimeOffset) ||
        type == typeof(TimeSpan);

    private static void ThrowIfTrailingBytes(int totalLength, int bytesRead)
    {
        if (bytesRead != totalLength)
        {
            throw new MessagePackSerializationException("Trailing bytes after serialized value.");
        }
    }

    private static void ThrowChangingValues(Type type, Exception? innerException = null) =>
        throw new MessagePackSerializationException(
            $"Type '{type.FullName}' cannot be serialized without changing constructor-bound get-only values.",
            innerException);

    private static class DeclaredTypeGuardCache<T>
    {
        public static ConstructorReplayGuard? Guard;
    }
}
