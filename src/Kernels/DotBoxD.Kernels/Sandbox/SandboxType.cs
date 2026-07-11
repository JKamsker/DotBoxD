using System.Text;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox;

public sealed record SandboxType(string Name, IReadOnlyList<SandboxType> Arguments)
{
    private string _name = RequireName(Name);
    private IReadOnlyList<SandboxType> _arguments = CopyArguments(Name, Arguments);

    public string Name
    {
        get => _name;
        init
        {
            var name = RequireName(value);
            RequireValidRecordShape(name, _arguments, nameof(value));
            _name = name;
        }
    }

    public IReadOnlyList<SandboxType> Arguments
    {
        get => _arguments;
        init
        {
            var arguments = CopyArguments(value);
            RequireValidRecordShape(_name, arguments, nameof(value));
            _arguments = arguments;
        }
    }

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
    public static SandboxType Guid { get; } = Scalar("Guid");
    public static SandboxType SandboxPath { get; } = Scalar("SandboxPath");
    public static SandboxType SandboxUri { get; } = Scalar("SandboxUri");

    public const string RecordName = "Record";

    public static SandboxType Scalar(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return new(name, []);
    }

    public static SandboxType List(SandboxType item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new("List", [item]);
    }

    public static SandboxType Map(SandboxType key, SandboxType value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        return new("Map", [key, value]);
    }

    /// <summary>
    /// A composite record/object type: an ordered, positional list of field types (≥ 1). Field names are
    /// not part of the structural type — the analyzer and the host marshaling layer map fields to a C#
    /// DTO's declared member order. Records can nest (a field may itself be a record, list, or map).
    /// </summary>
    public static SandboxType Record(IReadOnlyList<SandboxType> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0)
        {
            throw new ArgumentException("Record types must declare at least one field.", nameof(fields));
        }

        return new(RecordName, fields);
    }

    public bool IsRecord => Arguments.Count > 0 && StringComparer.Ordinal.Equals(Name, RecordName);

    public static bool IsForbiddenName(string? name)
        => name is not null &&
           (ForbiddenNames.Contains(name) ||
            name.StartsWith("System.", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.", StringComparison.Ordinal));

    /// <summary>
    /// Structural predicate: a name denotes an opaque-id brand when it is a well-formed
    /// identifier that is not a built-in scalar, not a collection constructor, and not a
    /// forbidden CLR-shaped name. Whether a given brand is permitted for a particular run
    /// is a host/policy decision (see <c>SandboxPolicy.DeclaredOpaqueIdTypes</c>), not a
    /// structural one.
    /// </summary>
    public static bool IsKnownOpaqueId(string name)
        => IsWellFormedOpaqueIdName(name);

    public static bool IsWellFormedOpaqueIdName(string name)
        => SandboxTypeRules.IsWellFormedOpaqueIdName(name);

    // Open structural check: any well-formed opaque-id brand is accepted. Used by runtime value
    // validation, which sees only types from an already-policy-validated module.
    public bool IsKnown(int maxDepth = 8) => SandboxTypeRules.IsKnown(this, maxDepth, declaredOpaqueIdTypes: null);

    // Host-gated structural check: an opaque-id brand is accepted only when the host declared it.
    // Used by module validation (declaredOpaqueIdTypes from the policy) and, with an empty set, by
    // the binding registry (built-in scalars only).
    public bool IsKnown(IReadOnlySet<string> declaredOpaqueIdTypes, int maxDepth = 8)
        => SandboxTypeRules.IsKnown(this, maxDepth, declaredOpaqueIdTypes);

    // Strict structural check: built-in scalars and collections only, no opaque-id brands.
    public bool IsKnownBuiltIn(int maxDepth = 8) => SandboxTypeRules.IsKnownBuiltIn(this, maxDepth);

    public bool IsForbidden() => SandboxTypeRules.IsForbidden(this);

    public bool IsValidMapKey() => IsValidMapKey(declaredOpaqueIdTypes: null);

    public bool IsValidMapKey(IReadOnlySet<string>? declaredOpaqueIdTypes)
        => SandboxTypeRules.IsValidMapKey(this, declaredOpaqueIdTypes);

    private static string RequireName(string name)
        => name ?? throw new ArgumentNullException(nameof(name));

    private static IReadOnlyList<SandboxType> CopyArguments(
        string name,
        IReadOnlyList<SandboxType> arguments)
    {
        var snapshot = CopyArguments(arguments);
        RequireValidRecordShape(name, snapshot, nameof(arguments));
        return snapshot;
    }

    private static IReadOnlyList<SandboxType> CopyArguments(IReadOnlyList<SandboxType> arguments)
    {
        var snapshot = ModelCopy.List(arguments);
        for (var i = 0; i < snapshot.Count; i++)
        {
            if (snapshot[i] is null)
            {
                throw new ArgumentException("Type arguments must not contain null elements.", nameof(arguments));
            }
        }

        return snapshot;
    }

    private static void RequireValidRecordShape(
        string name,
        IReadOnlyList<SandboxType> arguments,
        string paramName)
    {
        if (StringComparer.Ordinal.Equals(name, RecordName) && arguments.Count == 0)
        {
            throw new ArgumentException("Record types must declare at least one field.", paramName);
        }
    }

    public bool Equals(SandboxType? other)
    {
        if (other is null ||
            !StringComparer.Ordinal.Equals(Name, other.Name) ||
            Arguments.Count != other.Arguments.Count)
        {
            return false;
        }

        for (var i = 0; i < Arguments.Count; i++)
        {
            if (!Arguments[i].Equals(other.Arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name, StringComparer.Ordinal);
        foreach (var argument in Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        if (Arguments.Count == 0)
        {
            return Name;
        }

        var builder = new StringBuilder(Name);
        builder.Append('<');
        for (var i = 0; i < Arguments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Arguments[i].ToString());
        }

        builder.Append('>');
        return builder.ToString();
    }

}
