using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using MessagePack;

namespace DotBoxD.Codecs.MessagePack;

internal sealed class ConstructorReplayGuard
{
    private const int SerializedReplayState = -1;
    private static readonly ConcurrentDictionary<Type, ConstructorReplayGuard> Guards = new();
    private static readonly ConstructorReplayGuard None = new(null, [], [], [], useSerializedReplay: false);

    private readonly ConstructorInfo? _constructor;
    private readonly ParameterInfo[] _parameters;
    private readonly PropertyInfo[] _parameterProperties;
    private readonly PropertyInfo[] _boundProperties;
    private Func<object, bool>? _validator;
    private int _successfulReplays;
    private int _validatorCreationState;

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
        _validatorCreationState = useSerializedReplay ? SerializedReplayState : 0;
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
        if (ShouldBypass(declaredType))
        {
            return false;
        }

        var runtimeType = declaredType.IsSealed ? declaredType : value.GetType();
        if (ShouldBypass(runtimeType))
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

        if (guard._validatorCreationState == SerializedReplayState)
        {
            guard.SerializeWithReplayValidation(writer, value, options);
            return true;
        }

        guard.ValidateSimple(value);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ValidateSimple<T>(T value)
    {
        var fastValidator = _validatorCreationState ==
            ConstructorReplayValidatorAdmission.CreationStartedState
            ? Volatile.Read(ref _validator)
            : null;
        if (fastValidator is not null)
        {
            ValidateFast(fastValidator, value!);
            return;
        }

        ThrowIfConstructorReplayChangesValues(value);
        if (ConstructorReplayValidatorAdmission.TryClaimCreation(
            ref _successfulReplays,
            ref _validatorCreationState))
        {
            ConstructorReplayValidatorAdmission.Publish(
                this,
                ref _successfulReplays,
                ref _validatorCreationState,
                _constructor!,
                _parameterProperties,
                _boundProperties);
        }
    }

    internal void PublishValidator(Func<object, bool> validator) =>
        Volatile.Write(ref _validator, validator);

    private static bool ShouldBypass(Type type) => type.IsValueType || type == typeof(string);

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
            boundProperties.Any(static property =>
                !ConstructorReplayValidatorCompiler.SupportsTypedEquality(property.PropertyType)));
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

    private static void ValidateFast(Func<object, bool> validator, object value)
    {
        bool valuesMatch;
        try
        {
            valuesMatch = validator(value);
        }
        catch (Exception ex)
        {
            ThrowChangingValues(value.GetType(), ex);
            return;
        }

        if (!valuesMatch)
        {
            ThrowChangingValues(value.GetType());
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
