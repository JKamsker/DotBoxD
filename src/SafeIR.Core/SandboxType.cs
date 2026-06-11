using System.Text;

namespace SafeIR;

public sealed record SandboxType(string Name, IReadOnlyList<SandboxType> Arguments)
{
    private static readonly HashSet<string> AllowedScalars = new(StringComparer.Ordinal) {
        "Unit", "Bool", "I32", "I64", "F32", "F64", "Decimal", "String", "Bytes",
        "SandboxPath", "SandboxUri", "PlayerId", "ItemId", "QuestId", "MapId", "Command"
    };

    private static readonly HashSet<string> ForbiddenNames = new(StringComparer.OrdinalIgnoreCase) {
        "Object", "Dynamic", "Type", "Assembly", "MemberInfo", "MethodInfo", "PropertyInfo",
        "FieldInfo", "ConstructorInfo", "Module", "RuntimeTypeHandle", "RuntimeMethodHandle",
        "RuntimeFieldHandle", "Delegate", "Expression", "IQueryable", "IServiceProvider",
        "ServiceProvider", "Stream", "TextReader", "TextWriter", "FileInfo", "DirectoryInfo",
        "DriveInfo", "HttpClient", "Socket", "DbConnection", "DbContext", "Process",
        "Thread", "Task", "CancellationTokenSource", "IntPtr", "UIntPtr", "SafeHandle",
        "Span", "Memory", "Pointer"
    };

    public static SandboxType Unit { get; } = Scalar("Unit");
    public static SandboxType Bool { get; } = Scalar("Bool");
    public static SandboxType I32 { get; } = Scalar("I32");
    public static SandboxType I64 { get; } = Scalar("I64");
    public static SandboxType F64 { get; } = Scalar("F64");
    public static SandboxType String { get; } = Scalar("String");
    public static SandboxType SandboxPath { get; } = Scalar("SandboxPath");
    public static SandboxType Command { get; } = Scalar("Command");

    public static SandboxType Scalar(string name) => new(name, []);

    public static SandboxType List(SandboxType item) => new("List", [item]);

    public static bool IsForbiddenName(string name)
        => ForbiddenNames.Contains(name) ||
           name.StartsWith("System.", StringComparison.Ordinal) ||
           name.StartsWith("Microsoft.", StringComparison.Ordinal);

    public bool IsKnown(int maxDepth = 8) => IsKnown(this, 0, maxDepth);

    public bool IsForbidden() => IsForbidden(this);

    public override string ToString()
    {
        if (Arguments.Count == 0) {
            return Name;
        }

        var builder = new StringBuilder(Name);
        builder.Append('<');
        builder.Append(string.Join(",", Arguments.Select(a => a.ToString())));
        builder.Append('>');
        return builder.ToString();
    }

    private static bool IsKnown(SandboxType type, int depth, int maxDepth)
    {
        if (depth > maxDepth || IsForbiddenName(type.Name)) {
            return false;
        }

        if (AllowedScalars.Contains(type.Name)) {
            return type.Arguments.Count == 0;
        }

        return type.Name switch {
            "Option" or "List" => type.Arguments.Count == 1 && type.Arguments.All(a => IsKnown(a, depth + 1, maxDepth)),
            "Result" or "Map" => type.Arguments.Count == 2 && type.Arguments.All(a => IsKnown(a, depth + 1, maxDepth)),
            "Tuple" => type.Arguments.Count is >= 2 and <= 8 && type.Arguments.All(a => IsKnown(a, depth + 1, maxDepth)),
            _ => false
        };
    }

    private static bool IsForbidden(SandboxType type)
        => IsForbiddenName(type.Name) || type.Arguments.Any(IsForbidden);
}
