using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Runtime;

internal static partial class CompiledBindingDispatcher
{
    private static void ValidateArguments(BindingDescriptor descriptor, IReadOnlyList<SandboxValue> args)
    {
        if (args.Count != descriptor.Parameters.Count)
        {
            throw ArgumentCountMismatch(descriptor);
        }

        for (var i = 0; i < descriptor.Parameters.Count; i++)
        {
            if (!TypeMatches(args[i], descriptor.Parameters[i]))
            {
                throw ArgumentTypeMismatch(descriptor);
            }
        }
    }

    private static void ValidateArguments(BindingDescriptor descriptor, SandboxValue arg0)
    {
        if (descriptor.Parameters.Count != 1)
        {
            throw ArgumentCountMismatch(descriptor);
        }

        if (!TypeMatches(arg0, descriptor.Parameters[0]))
        {
            throw ArgumentTypeMismatch(descriptor);
        }
    }

    private static void ValidateArguments(BindingDescriptor descriptor, SandboxValue arg0, SandboxValue arg1)
    {
        if (descriptor.Parameters.Count != 2)
        {
            throw ArgumentCountMismatch(descriptor);
        }

        if (!TypeMatches(arg0, descriptor.Parameters[0]) || !TypeMatches(arg1, descriptor.Parameters[1]))
        {
            throw ArgumentTypeMismatch(descriptor);
        }
    }

    internal static void ValidateArguments(
        BindingDescriptor descriptor,
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2)
    {
        if (descriptor.Parameters.Count != 3)
        {
            throw ArgumentCountMismatch(descriptor);
        }

        if (!TypeMatches(arg0, descriptor.Parameters[0]) ||
            !TypeMatches(arg1, descriptor.Parameters[1]) ||
            !TypeMatches(arg2, descriptor.Parameters[2]))
        {
            throw ArgumentTypeMismatch(descriptor);
        }
    }

    private static bool TypeMatches(SandboxValue value, SandboxType expected)
    {
        if (expected.Arguments.Count == 0)
        {
            return ScalarTypeMatches(value, expected.Name);
        }

        if (IsListType(expected))
        {
            return value is ListValue list && list.ItemType.Equals(expected.Arguments[0]);
        }

        if (IsMapType(expected))
        {
            return value is MapValue map &&
                   map.KeyType.Equals(expected.Arguments[0]) &&
                   map.ValueType.Equals(expected.Arguments[1]);
        }

        if (expected.IsRecord && value is RecordValue record)
        {
            return RecordTypeMatches(record, expected);
        }

        return false;
    }

    private static bool IsListType(SandboxType type)
        => type.Name == "List" && type.Arguments.Count == 1;

    private static bool IsMapType(SandboxType type)
        => type.Name == "Map" && type.Arguments.Count == 2;

    private static bool ScalarTypeMatches(SandboxValue value, string expectedName)
    {
        if (value is OpaqueIdValue opaque)
        {
            return string.Equals(opaque.TypeName, expectedName, StringComparison.Ordinal);
        }

        if (TryPrimitiveScalarName(value, out var scalarName))
        {
            return expectedName == scalarName;
        }

        return TryResourceScalarName(value, out scalarName) && expectedName == scalarName;
    }

    private static bool TryPrimitiveScalarName(SandboxValue value, out string name)
    {
        name = value switch
        {
            UnitValue => SandboxType.Unit.Name,
            BoolValue => SandboxType.Bool.Name,
            I32Value => SandboxType.I32.Name,
            I64Value => SandboxType.I64.Name,
            F64Value => SandboxType.F64.Name,
            StringValue => SandboxType.String.Name,
            GuidValue => SandboxType.Guid.Name,
            _ => string.Empty
        };
        return name.Length != 0;
    }

    private static bool TryResourceScalarName(SandboxValue value, out string name)
    {
        name = value switch
        {
            SandboxPathValue => SandboxType.SandboxPath.Name,
            SandboxUriValue => SandboxType.SandboxUri.Name,
            _ => string.Empty
        };
        return name.Length != 0;
    }

    private static bool RecordTypeMatches(RecordValue record, SandboxType expected)
    {
        if (record.Fields.Count != expected.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < record.Fields.Count; i++)
        {
            if (!TypeMatches(record.Fields[i], expected.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static SandboxRuntimeException ArgumentCountMismatch(BindingDescriptor descriptor)
        => new(new SandboxError(
            SandboxErrorCode.ValidationError,
            $"binding '{descriptor.Id}' argument count does not match verified plan"));

    private static SandboxRuntimeException ArgumentTypeMismatch(BindingDescriptor descriptor)
        => new(new SandboxError(
            SandboxErrorCode.ValidationError,
            $"binding '{descriptor.Id}' argument type does not match verified plan"));
}
