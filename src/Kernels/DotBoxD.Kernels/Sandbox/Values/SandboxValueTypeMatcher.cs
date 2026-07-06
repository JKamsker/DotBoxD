namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValueTypeMatcher
{
    /// <summary>
    /// Matches the current validation frame without materializing <see cref="SandboxValue.Type"/>.
    /// Record fields are validated by the caller's recursive stack, so records only need an arity check here.
    /// </summary>
    public static bool MatchesValidationFrame(SandboxValue value, SandboxType expectedType)
    {
        if (expectedType.Arguments.Count == 0)
        {
            return ScalarMatches(value, expectedType.Name);
        }

        if (IsListType(expectedType))
        {
            return value is ListValue { ItemType: { } itemType } &&
                   itemType.Equals(expectedType.Arguments[0]);
        }

        if (IsMapType(expectedType))
        {
            return value is MapValue { KeyType: { } keyType, ValueType: { } valueType } &&
                   keyType.Equals(expectedType.Arguments[0]) &&
                   valueType.Equals(expectedType.Arguments[1]);
        }

        return expectedType.IsRecord &&
               value is RecordValue record &&
               record.Fields.Count == expectedType.Arguments.Count;
    }

    private static bool IsListType(SandboxType type)
        => type.Name == "List" && type.Arguments.Count == 1;

    private static bool IsMapType(SandboxType type)
        => type.Name == "Map" && type.Arguments.Count == 2;

    public static bool MatchesExactType(SandboxValue value, SandboxType expectedType)
    {
        if (expectedType.Name == SandboxType.RecordName)
        {
            return value is RecordValue record && RecordMatchesExactType(record, expectedType);
        }

        return MatchesValidationFrame(value, expectedType);
    }

    private static bool RecordMatchesExactType(RecordValue record, SandboxType expectedType)
    {
        if (record.Fields.Count != expectedType.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < record.Fields.Count; i++)
        {
            if (!MatchesExactType(record.Fields[i], expectedType.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ScalarMatches(SandboxValue value, string expectedName)
    {
        if (value is OpaqueIdValue id)
        {
            return string.Equals(id.TypeName, expectedName, StringComparison.Ordinal);
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
}
