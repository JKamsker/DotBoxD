using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox;

internal static class SandboxScalarValueValidator
{
    public static bool IsBuiltInScalarType(SandboxValue value, SandboxType expectedType)
    {
        if (TryBuiltInScalarType(value, out var actualType))
        {
            return ReferenceEquals(expectedType, actualType);
        }

        return false;
    }

    public static void RequireScalarInvariants(
        SandboxValue value,
        SandboxErrorCode errorCode,
        string message)
    {
        switch (value)
        {
            case StringValue { Value: null }:
                throw Error(errorCode, message);
            case F64Value number when !double.IsFinite(number.Value):
                throw Error(errorCode, message);
            case SandboxPathValue path:
                RequirePortablePath(path, errorCode, message);
                break;
            case SandboxUriValue uri:
                RequireSandboxUri(uri, errorCode, message);
                break;
        }
    }

    public static void RequireOpaqueId(
        OpaqueIdValue id,
        SandboxErrorCode errorCode,
        string message)
    {
        if (!SandboxType.IsKnownOpaqueId(id.TypeName) ||
            !SandboxLiteralConstraints.IsOpaqueId(id.Value))
        {
            throw Error(errorCode, message);
        }
    }

    private static bool TryBuiltInScalarType(SandboxValue value, out SandboxType type)
    {
        if (TryPrimitiveScalarType(value, out type))
        {
            return true;
        }

        return TryResourceScalarType(value, out type);
    }

    private static bool TryPrimitiveScalarType(SandboxValue value, out SandboxType type)
    {
        type = value switch
        {
            UnitValue => SandboxType.Unit,
            BoolValue => SandboxType.Bool,
            I32Value => SandboxType.I32,
            I64Value => SandboxType.I64,
            F64Value => SandboxType.F64,
            StringValue => SandboxType.String,
            GuidValue => SandboxType.Guid,
            _ => null!
        };
        return type is not null;
    }

    private static bool TryResourceScalarType(SandboxValue value, out SandboxType type)
    {
        type = value switch
        {
            SandboxPathValue => SandboxType.SandboxPath,
            SandboxUriValue => SandboxType.SandboxUri,
            _ => null!
        };
        return type is not null;
    }

    private static void RequirePortablePath(
        SandboxPathValue path,
        SandboxErrorCode errorCode,
        string message)
    {
        if (path.Value?.RelativePath is not { } relativePath ||
            !SandboxLiteralConstraints.IsPortableRelativePath(relativePath))
        {
            throw Error(errorCode, message);
        }
    }

    private static void RequireSandboxUri(
        SandboxUriValue uri,
        SandboxErrorCode errorCode,
        string message)
    {
        if (uri.Value?.Value is not { } valueText ||
            !SandboxLiteralConstraints.IsSandboxUri(valueText))
        {
            throw Error(errorCode, message);
        }
    }

    private static SandboxRuntimeException Error(SandboxErrorCode code, string message)
        => new(new SandboxError(code, message));
}
