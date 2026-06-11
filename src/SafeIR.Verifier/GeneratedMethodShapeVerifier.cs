namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class GeneratedMethodShapeVerifier
{
    private const string Runtime = "SafeIR.Runtime.CompiledRuntime";
    private const string Context = "SafeIR.SandboxContext";
    private const string Value = "SafeIR.SandboxValue";
    private const string Int32 = "System.Int32";
    private const string Void = "System.Void";
    private const string ValidateInput = $"{Runtime}.ValidateEntrypointInput({Value},{Int32}):{Void}";
    private const string EnterCall = $"{Runtime}.EnterCall({Context}):{Void}";
    private const string ExitCall = $"{Runtime}.ExitCall({Context}):{Void}";
    private const string ChargeFuel = $"{Runtime}.ChargeFuel({Context},{Int32}):{Void}";

    public static void VerifyBody(
        MetadataReader reader,
        MethodBodyBlock body,
        string methodName,
        List<VerificationDiagnostic> diagnostics)
    {
        var calls = CalledMembers(reader, body).ToHashSet(StringComparer.Ordinal);
        if (methodName == "Execute") {
            Require(calls, ValidateInput, diagnostics, "Execute must validate entrypoint input shape");
            return;
        }

        if (methodName.StartsWith("Fn_", StringComparison.Ordinal)) {
            Require(calls, EnterCall, diagnostics, $"method '{methodName}' must enter the call meter");
            Require(calls, ExitCall, diagnostics, $"method '{methodName}' must exit the call meter");
            Require(calls, ChargeFuel, diagnostics, $"method '{methodName}' must charge fuel");
        }
    }

    private static IEnumerable<string> CalledMembers(MetadataReader reader, MethodBodyBlock body)
    {
        var il = body.GetILReader();
        while (il.RemainingBytes > 0) {
            var opcode = ReadOpCode(ref il);
            if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj) {
                var handle = MetadataTokens.EntityHandle(il.ReadInt32());
                yield return MetadataName.MemberSignature(reader, handle).Signature;
                continue;
            }

            SkipOperand(opcode, ref il);
        }
    }

    private static void Require(
        HashSet<string> calls,
        string signature,
        List<VerificationDiagnostic> diagnostics,
        string message)
    {
        if (!calls.Contains(signature)) {
            diagnostics.Add(new VerificationDiagnostic("V-COMPILED-SHAPE", message));
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        return first == 0xFE ? (ILOpCode)(0xFE00 | il.ReadByte()) : (ILOpCode)first;
    }

    private static void SkipOperand(ILOpCode opcode, ref BlobReader il)
    {
        if (opcode == ILOpCode.Switch) {
            var count = il.ReadInt32();
            for (var i = 0; i < count; i++) {
                _ = il.ReadInt32();
            }

            return;
        }

        if (opcode.IsBranch()) {
            if (opcode.GetBranchOperandSize() == 1) {
                _ = il.ReadSByte();
            }
            else {
                _ = il.ReadInt32();
            }

            return;
        }

        switch (opcode) {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr or ILOpCode.Newarr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Ldc_r8:
                _ = il.ReadDouble();
                break;
        }
    }
}
