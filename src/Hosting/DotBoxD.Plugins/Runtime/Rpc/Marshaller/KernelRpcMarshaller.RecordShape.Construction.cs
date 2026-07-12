using System.Collections;
using System.Reflection;

namespace DotBoxD.Plugins.Runtime.Rpc;

public static partial class KernelRpcMarshaller
{
    private sealed partial class RecordShape
    {
        public object?[] GetValues(object instance)
        {
            var values = new object?[Fields.Count];
            for (var i = 0; i < Fields.Count; i++)
            {
                try
                {
                    values[i] = GetValue(instance, i);
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException { InnerException: { } target } ? target : ex;
                    throw new NotSupportedException(
                        $"Server extension DTO '{_type}' field '{Fields[i].Name}' could not be read.",
                        inner);
                }
            }

            return values;
        }

        public void RejectUnreconstructibleOutboundValue(object?[] arguments)
        {
            if (_recordFactory is not null || !HasReadOnlyField())
            {
                return;
            }

            _ = ConstructFromArguments(arguments);
        }

        private bool HasReadOnlyField()
        {
            for (var i = 0; i < Fields.Count; i++)
            {
                if (!Fields[i].IsSettable)
                {
                    return true;
                }
            }

            return false;
        }

        private object ConstructFromArguments(object?[] arguments)
        {
            var instance = ConstructInstance(arguments);
            for (var i = 0; i < Fields.Count; i++)
            {
                if (Fields[i].IsSettable)
                {
                    Fields[i].SetValue(instance, arguments[i]);
                }
            }

            for (var i = 0; i < Fields.Count; i++)
            {
                if (!Fields[i].IsSettable)
                {
                    VerifyReadOnlyField(instance, Fields[i], arguments[i]);
                }
            }

            return instance;
        }

        private object ConstructInstance(object?[] arguments)
        {
            if (_constructor is null)
            {
                if (!HasPublicParameterlessConstructor())
                {
                    throw new NotSupportedException(
                        $"Server extension DTO '{_type}' does not expose a public parameterless constructor " +
                        "or a public constructor matching its public fields.");
                }

                return Activator.CreateInstance(_type)
                    ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
            }

            var parameters = _constructor.GetParameters();
            var constructorArguments = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var fieldIndex = _constructorMap[i];
                if (fieldIndex < 0)
                {
                    constructorArguments[i] = DefaultParameterValue(parameters[i]);
                    continue;
                }

                constructorArguments[i] = arguments[fieldIndex];
            }

            return _constructor.Invoke(constructorArguments)
                ?? throw new NotSupportedException($"Server extension could not construct '{_type}'.");
        }

        private void VerifyReadOnlyField(object instance, RecordMember field, object? expected)
        {
            var actual = field.GetValue(instance);
            if (!ValuesEqual(actual, expected))
            {
                throw new NotSupportedException(
                    $"Server extension DTO '{_type}' field '{field.Name}' is private or read-only and could not be reconstructed.");
            }
        }

        private static bool ValuesEqual(object? actual, object? expected)
        {
            if (ReferenceEquals(actual, expected))
            {
                return true;
            }

            if (actual is null || expected is null)
            {
                return false;
            }

            if (actual is Array actualArray &&
                expected is Array expectedArray &&
                actualArray.GetType() == expectedArray.GetType())
            {
                return StructuralComparisons.StructuralEqualityComparer.Equals(actualArray, expectedArray);
            }

            return Equals(actual, expected);
        }
    }
}
