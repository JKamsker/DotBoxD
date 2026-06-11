namespace SafeIR.Verifier;

public sealed record VerificationPolicy(
    IReadOnlySet<string> AllowedAssemblies,
    IReadOnlySet<string> AllowedTypes,
    IReadOnlySet<string> AllowedMembers,
    IReadOnlySet<string> ForbiddenTypePrefixes,
    string VerifierVersion)
{
    public static VerificationPolicy BoxedValueDefaults() => new(
        new HashSet<string>(StringComparer.Ordinal) {
            "System.Private.CoreLib", "System.Runtime", "SafeIR.Core", "SafeIR.Runtime"
        },
        new HashSet<string>(StringComparer.Ordinal) {
            "System.Object", "System.Void", "System.Boolean", "System.Int32", "System.String",
            "System.Double",
            "SafeIR.SandboxValue", "SafeIR.SandboxContext", "SafeIR.Runtime.CompiledRuntime"
        },
        new HashSet<string>(StringComparer.Ordinal) {
            "SafeIR.Runtime.CompiledRuntime.ChargeFuel",
            "SafeIR.Runtime.CompiledRuntime.EnterCall",
            "SafeIR.Runtime.CompiledRuntime.ExitCall",
            "SafeIR.Runtime.CompiledRuntime.GetInputArgument",
            "SafeIR.Runtime.CompiledRuntime.I32",
            "SafeIR.Runtime.CompiledRuntime.F64",
            "SafeIR.Runtime.CompiledRuntime.Bool",
            "SafeIR.Runtime.CompiledRuntime.StringConst",
            "SafeIR.Runtime.CompiledRuntime.AsI32",
            "SafeIR.Runtime.CompiledRuntime.AsBool",
            "SafeIR.Runtime.CompiledRuntime.AddI32",
            "SafeIR.Runtime.CompiledRuntime.SubI32",
            "SafeIR.Runtime.CompiledRuntime.MulI32",
            "SafeIR.Runtime.CompiledRuntime.DivI32",
            "SafeIR.Runtime.CompiledRuntime.RemI32",
            "SafeIR.Runtime.CompiledRuntime.NegI32",
            "SafeIR.Runtime.CompiledRuntime.NotBool",
            "SafeIR.Runtime.CompiledRuntime.Eq",
            "SafeIR.Runtime.CompiledRuntime.Ne",
            "SafeIR.Runtime.CompiledRuntime.LtI32",
            "SafeIR.Runtime.CompiledRuntime.LteI32",
            "SafeIR.Runtime.CompiledRuntime.GtI32",
            "SafeIR.Runtime.CompiledRuntime.GteI32",
            "SafeIR.Runtime.CompiledRuntime.And",
            "SafeIR.Runtime.CompiledRuntime.Or",
            "SafeIR.Runtime.CompiledRuntime.StringLength",
            "SafeIR.Runtime.CompiledRuntime.ConcatString",
            "SafeIR.Runtime.CompiledRuntime.AbsI32",
            "SafeIR.Runtime.CompiledRuntime.MinI32",
            "SafeIR.Runtime.CompiledRuntime.MaxI32",
            "SafeIR.Runtime.CompiledRuntime.ClampI32",
            "SafeIR.Runtime.CompiledRuntime.SqrtF64",
            "SafeIR.Runtime.CompiledRuntime.FloorF64",
            "SafeIR.Runtime.CompiledRuntime.CeilF64",
            "SafeIR.Runtime.CompiledRuntime.RoundF64"
        },
        new HashSet<string>(StringComparer.Ordinal) {
            "System.IO.", "System.Net.", "System.Reflection.", "System.Runtime.Loader.",
            "System.Runtime.InteropServices.", "System.Diagnostics.", "System.Threading.",
            "System.Threading.Tasks.", "System.Activator", "System.Environment",
            "System.GC", "System.Delegate", "System.Linq.Expressions.", "Microsoft.CSharp."
        },
        "safe-ir-verifier-1");

    public bool IsMemberAllowed(string typeName, string memberName)
        => AllowedMembers.Contains($"{typeName}.{memberName}");
}
