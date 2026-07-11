namespace DotBoxD.Kernels.Sandbox;

internal static class SandboxTypeRules
{
    private const int DefaultMaxDepth = 8;
    private const int MaxOpaqueIdNameLength = 64;

    private static readonly HashSet<string> AllowedScalars = new(StringComparer.Ordinal) {
        "Unit", "Bool", "I32", "I64", "F64", "String", "Guid",
        "SandboxPath", "SandboxUri"
    };

    private static readonly HashSet<string> MapKeyScalars = new(StringComparer.Ordinal) {
        "Bool", "I32", "I64", "String", "SandboxPath", "SandboxUri"
    };

    private static readonly IReadOnlySet<string> EmptyOpaqueIdTypes =
        new HashSet<string>(StringComparer.Ordinal);

    public static bool IsWellFormedOpaqueIdName(string name)
    {
        if (HasInvalidOpaqueIdNamePrefix(name))
        {
            return false;
        }

        for (var i = 1; i < name.Length; i++)
        {
            var character = name[i];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsKnown(
        SandboxType type,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
        => IsKnown(type, 0, maxDepth, declaredOpaqueIdTypes);

    public static bool IsKnownBuiltIn(SandboxType type, int maxDepth)
        => IsKnown(type, 0, maxDepth, EmptyOpaqueIdTypes);

    public static bool IsForbidden(SandboxType type)
        => IsForbidden(type, 0, DefaultMaxDepth);

    private static bool IsForbidden(SandboxType type, int depth, int maxDepth)
    {
        if (depth > maxDepth || SandboxType.IsForbiddenName(type.Name))
        {
            return true;
        }

        for (var i = 0; i < type.Arguments.Count; i++)
        {
            if (IsForbidden(type.Arguments[i], depth + 1, maxDepth))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsValidMapKey(SandboxType type, IReadOnlySet<string>? declaredOpaqueIdTypes)
        => type.Arguments.Count == 0 &&
           (MapKeyScalars.Contains(type.Name) || IsAcceptedOpaqueIdBrand(type.Name, declaredOpaqueIdTypes));

    private static bool HasInvalidOpaqueIdNamePrefix(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return true;
        }

        if (name.Length > MaxOpaqueIdNameLength || AllowedScalars.Contains(name))
        {
            return true;
        }

        if (name is "List" or "Map" or SandboxType.RecordName)
        {
            return true;
        }

        return SandboxType.IsForbiddenName(name) || !char.IsAsciiLetterUpper(name[0]);
    }

    private static bool IsAcceptedOpaqueIdBrand(string name, IReadOnlySet<string>? declaredOpaqueIdTypes)
        => IsWellFormedOpaqueIdName(name) &&
           (declaredOpaqueIdTypes is null || declaredOpaqueIdTypes.Contains(name));

    private static bool IsKnown(
        SandboxType type,
        int depth,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
    {
        if (depth > maxDepth ||
            string.IsNullOrEmpty(type.Name) ||
            SandboxType.IsForbiddenName(type.Name))
        {
            return false;
        }

        if (type.Arguments.Count == 0)
        {
            return IsKnownScalar(type, declaredOpaqueIdTypes);
        }

        return IsKnownComposite(type, depth, maxDepth, declaredOpaqueIdTypes);
    }

    private static bool IsKnownScalar(SandboxType type, IReadOnlySet<string>? declaredOpaqueIdTypes)
        => AllowedScalars.Contains(type.Name) ||
           IsAcceptedOpaqueIdBrand(type.Name, declaredOpaqueIdTypes);

    private static bool IsKnownComposite(
        SandboxType type,
        int depth,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
        => type.Name switch
        {
            "List" => IsKnownList(type, depth, maxDepth, declaredOpaqueIdTypes),
            SandboxType.RecordName => IsKnownRecord(type, depth, maxDepth, declaredOpaqueIdTypes),
            "Map" => IsKnownMap(type, depth, maxDepth, declaredOpaqueIdTypes),
            _ => false
        };

    private static bool IsKnownList(
        SandboxType type,
        int depth,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
        => type.Arguments.Count == 1 &&
           IsKnown(type.Arguments[0], depth + 1, maxDepth, declaredOpaqueIdTypes);

    private static bool IsKnownRecord(
        SandboxType type,
        int depth,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
    {
        for (var i = 0; i < type.Arguments.Count; i++)
        {
            if (!IsKnown(type.Arguments[i], depth + 1, maxDepth, declaredOpaqueIdTypes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownMap(
        SandboxType type,
        int depth,
        int maxDepth,
        IReadOnlySet<string>? declaredOpaqueIdTypes)
        => type.Arguments.Count == 2 &&
           IsValidMapKey(type.Arguments[0], declaredOpaqueIdTypes) &&
           IsKnown(type.Arguments[0], depth + 1, maxDepth, declaredOpaqueIdTypes) &&
           IsKnown(type.Arguments[1], depth + 1, maxDepth, declaredOpaqueIdTypes);
}
