using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingCompiledTargetValidator
{
    private const string RuntimeStubKind = "RuntimeStub";
    private const string ApprovedCompiledRuntimeType = "DotBoxD.Kernels.Runtime.CompiledRuntime";
    private const string GenericBindingStub = "CallBinding";

    private static readonly HashSet<string> ApprovedCompiledRuntimeMethods = new(StringComparer.Ordinal) {
        GenericBindingStub,
        "Int32ToStringInvariant",
        "StringLength",
        "ConcatString",
        "AbsI32",
        "MinI32",
        "MaxI32",
        "ClampI32",
        "SqrtF64",
        "FloorF64",
        "CeilF64",
        "RoundF64"
    };
    private static readonly IReadOnlyDictionary<string, (SandboxType Return, SandboxType[] Parameters)> DirectCompiledSignatures =
        new Dictionary<string, (SandboxType Return, SandboxType[] Parameters)>(StringComparer.Ordinal)
        {
            ["Int32ToStringInvariant"] = (SandboxType.String, [SandboxType.I32]),
            ["StringLength"] = (SandboxType.I32, [SandboxType.String]),
            ["ConcatString"] = (SandboxType.String, [SandboxType.String, SandboxType.String]),
            ["AbsI32"] = (SandboxType.I32, [SandboxType.I32]),
            ["MinI32"] = (SandboxType.I32, [SandboxType.I32, SandboxType.I32]),
            ["MaxI32"] = (SandboxType.I32, [SandboxType.I32, SandboxType.I32]),
            ["ClampI32"] = (SandboxType.I32, [SandboxType.I32, SandboxType.I32, SandboxType.I32]),
            ["SqrtF64"] = (SandboxType.F64, [SandboxType.F64]),
            ["FloorF64"] = (SandboxType.F64, [SandboxType.F64]),
            ["CeilF64"] = (SandboxType.F64, [SandboxType.F64]),
            ["RoundF64"] = (SandboxType.F64, [SandboxType.F64])
        };

    public static void Validate(BindingDescriptor binding, List<SandboxDiagnostic> diagnostics)
        => Validate(
            binding.Id,
            binding.Compiled,
            binding.Safety,
            binding.AuditLevel,
            binding.ReturnType,
            binding.Parameters,
            diagnostics);

    public static void Validate(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
        => Validate(
            binding.Id,
            binding.Compiled,
            binding.Safety,
            binding.AuditLevel,
            binding.ReturnType,
            binding.Parameters,
            diagnostics);

    private static void Validate(
        string bindingId,
        CompiledBinding compiled,
        BindingSafety safety,
        AuditLevel auditLevel,
        SandboxType returnType,
        IReadOnlyList<SandboxType> parameters,
        List<SandboxDiagnostic> diagnostics)
    {
        if (compiled.Kind != RuntimeStubKind)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{bindingId}' has unsupported compiled target kind"));
        }

        if (string.IsNullOrWhiteSpace(compiled.Type) ||
            string.IsNullOrWhiteSpace(compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{bindingId}' has an incomplete compiled target"));
            return;
        }

        if (compiled.Type != ApprovedCompiledRuntimeType ||
            !ApprovedCompiledRuntimeMethods.Contains(compiled.Method))
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{bindingId}' points compiled code outside the approved runtime stub surface"));
            return;
        }

        if (compiled.Method != GenericBindingStub && safety != BindingSafety.PureIntrinsic)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{bindingId}' uses a direct compiled runtime method but is not a pure intrinsic"));
        }

        if (compiled.Method != GenericBindingStub && auditLevel != AuditLevel.None)
        {
            diagnostics.Add(new SandboxDiagnostic("E-BINDING-COMPILED", $"binding '{bindingId}' uses a direct compiled runtime method but requires binding audit"));
        }

        ValidateDirectCompiledSignature(bindingId, compiled, returnType, parameters, diagnostics);
    }

    private static void ValidateDirectCompiledSignature(
        string bindingId,
        CompiledBinding compiled,
        SandboxType returnType,
        IReadOnlyList<SandboxType> parameters,
        List<SandboxDiagnostic> diagnostics)
    {
        if (compiled.Method == GenericBindingStub)
        {
            return;
        }

        var expected = DirectCompiledSignature(compiled.Method);
        if (!returnType.Equals(expected.Return) ||
            parameters.Count != expected.Parameters.Length)
        {
            diagnostics.Add(DirectSignatureDiagnostic(bindingId));
            return;
        }

        for (var i = 0; i < expected.Parameters.Length; i++)
        {
            if (!parameters[i].Equals(expected.Parameters[i]))
            {
                diagnostics.Add(DirectSignatureDiagnostic(bindingId));
                return;
            }
        }
    }

    private static (SandboxType Return, SandboxType[] Parameters) DirectCompiledSignature(string method)
        => DirectCompiledSignatures.TryGetValue(method, out var signature)
            ? signature
            : (SandboxType.Unit, []);

    private static SandboxDiagnostic DirectSignatureDiagnostic(string bindingId)
        => new(
            "E-BINDING-COMPILED",
            $"binding '{bindingId}' direct compiled runtime signature does not match the binding shape");
}
