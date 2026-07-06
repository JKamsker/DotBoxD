using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

internal readonly record struct BinaryRuntimeCall(
    string Operator,
    string LeftType,
    string? RightType,
    StackKind LeftKind,
    StackKind RightKind,
    StackKind ResultKind,
    string RuntimeMethod)
{
    public bool Matches(string op, SandboxType? left, SandboxType? right)
        => string.Equals(Operator, op, StringComparison.Ordinal) &&
           string.Equals(LeftType, left?.Name, StringComparison.Ordinal) &&
           (RightType is null || string.Equals(RightType, right?.Name, StringComparison.Ordinal));
}

internal static class BinaryRuntimeCalls
{
    private static readonly BinaryRuntimeCall[] Raw =
    [
        I32("+", nameof(Kernels.Runtime.CompiledRuntime.AddI32Raw)),
        I32("-", nameof(Kernels.Runtime.CompiledRuntime.SubI32Raw)),
        I32("*", nameof(Kernels.Runtime.CompiledRuntime.MulI32Raw)),
        I32("/", nameof(Kernels.Runtime.CompiledRuntime.DivI32Raw)),
        I32("%", nameof(Kernels.Runtime.CompiledRuntime.RemI32Raw)),
        I64("+", StackKind.I64, nameof(Kernels.Runtime.CompiledRuntime.AddI64Raw)),
        I64("-", StackKind.I64, nameof(Kernels.Runtime.CompiledRuntime.SubI64Raw)),
        I64("*", StackKind.I64, nameof(Kernels.Runtime.CompiledRuntime.MulI64Raw)),
        I64("/", StackKind.I64, nameof(Kernels.Runtime.CompiledRuntime.DivI64Raw)),
        I64("%", StackKind.I64, nameof(Kernels.Runtime.CompiledRuntime.RemI64Raw)),
        F64("+", StackKind.F64, nameof(Kernels.Runtime.CompiledRuntime.AddF64Raw)),
        F64("-", StackKind.F64, nameof(Kernels.Runtime.CompiledRuntime.SubF64Raw)),
        F64("*", StackKind.F64, nameof(Kernels.Runtime.CompiledRuntime.MulF64Raw)),
        F64("/", StackKind.F64, nameof(Kernels.Runtime.CompiledRuntime.DivF64Raw)),
        I32("<", nameof(Kernels.Runtime.CompiledRuntime.LtI32Raw), StackKind.Bool, requireRightType: true),
        I32("<=", nameof(Kernels.Runtime.CompiledRuntime.LteI32Raw), StackKind.Bool, requireRightType: true),
        I32(">", nameof(Kernels.Runtime.CompiledRuntime.GtI32Raw), StackKind.Bool, requireRightType: true),
        I32(">=", nameof(Kernels.Runtime.CompiledRuntime.GteI32Raw), StackKind.Bool, requireRightType: true),
        I32("==", nameof(Kernels.Runtime.CompiledRuntime.EqI32Raw), StackKind.Bool, requireRightType: true),
        I32("!=", nameof(Kernels.Runtime.CompiledRuntime.NeI32Raw), StackKind.Bool, requireRightType: true),
        I64("<", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.LtI64Raw)),
        I64("<=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.LteI64Raw)),
        I64(">", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.GtI64Raw)),
        I64(">=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.GteI64Raw)),
        I64("==", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.EqI64Raw)),
        I64("!=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.NeI64Raw)),
        F64("<", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.LtF64Raw)),
        F64("<=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.LteF64Raw)),
        F64(">", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.GtF64Raw)),
        F64(">=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.GteF64Raw)),
        F64("==", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.EqF64Raw)),
        F64("!=", StackKind.Bool, nameof(Kernels.Runtime.CompiledRuntime.NeF64Raw)),
    ];

    private static readonly (string Operator, string Method)[] Boxed =
    [
        ("+", nameof(Kernels.Runtime.CompiledRuntime.Add)),
        ("-", nameof(Kernels.Runtime.CompiledRuntime.Sub)),
        ("*", nameof(Kernels.Runtime.CompiledRuntime.Mul)),
        ("/", nameof(Kernels.Runtime.CompiledRuntime.Div)),
        ("%", nameof(Kernels.Runtime.CompiledRuntime.Rem)),
        ("==", nameof(Kernels.Runtime.CompiledRuntime.Eq)),
        ("!=", nameof(Kernels.Runtime.CompiledRuntime.Ne)),
        ("<", nameof(Kernels.Runtime.CompiledRuntime.Lt)),
        ("<=", nameof(Kernels.Runtime.CompiledRuntime.Lte)),
        (">", nameof(Kernels.Runtime.CompiledRuntime.Gt)),
        (">=", nameof(Kernels.Runtime.CompiledRuntime.Gte)),
    ];

    public static bool TryGetRaw(string op, SandboxType? left, SandboxType? right, out BinaryRuntimeCall call)
    {
        foreach (var candidate in Raw)
        {
            if (candidate.Matches(op, left, right))
            {
                call = candidate;
                return true;
            }
        }

        call = default;
        return false;
    }

    public static bool TryGetBoxed(string op, out string method)
    {
        foreach (var candidate in Boxed)
        {
            if (string.Equals(candidate.Operator, op, StringComparison.Ordinal))
            {
                method = candidate.Method;
                return true;
            }
        }

        method = string.Empty;
        return false;
    }

    private static BinaryRuntimeCall I32(
        string op,
        string method,
        StackKind result = StackKind.I32,
        bool requireRightType = false)
        => new(op, "I32", requireRightType ? "I32" : null, StackKind.I32, StackKind.I32, result, method);

    private static BinaryRuntimeCall I64(string op, StackKind result, string method)
        => SameType(op, "I64", StackKind.I64, result, method);

    private static BinaryRuntimeCall F64(string op, StackKind result, string method)
        => SameType(op, "F64", StackKind.F64, result, method);

    private static BinaryRuntimeCall SameType(string op, string type, StackKind kind, StackKind result, string method)
        => new(op, type, type, kind, kind, result, method);
}
