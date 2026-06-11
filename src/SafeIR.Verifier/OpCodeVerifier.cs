namespace SafeIR.Verifier;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

internal static class OpCodeVerifier
{
    private static readonly HashSet<ILOpCode> Allowed = [
        ILOpCode.Nop, ILOpCode.Ldarg_0, ILOpCode.Ldarg_1, ILOpCode.Ldarg_2, ILOpCode.Ldarg_3,
        ILOpCode.Ldarg, ILOpCode.Ldarg_s, ILOpCode.Starg, ILOpCode.Starg_s,
        ILOpCode.Ldloc_0, ILOpCode.Ldloc_1, ILOpCode.Ldloc_2, ILOpCode.Ldloc_3,
        ILOpCode.Ldloc, ILOpCode.Ldloc_s, ILOpCode.Stloc_0, ILOpCode.Stloc_1, ILOpCode.Stloc_2, ILOpCode.Stloc_3,
        ILOpCode.Stloc, ILOpCode.Stloc_s, ILOpCode.Ldnull, ILOpCode.Ldc_i4, ILOpCode.Ldc_i4_s,
        ILOpCode.Ldc_i4_0, ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4_2, ILOpCode.Ldc_i4_3, ILOpCode.Ldc_i4_4,
        ILOpCode.Ldc_i4_5, ILOpCode.Ldc_i4_6, ILOpCode.Ldc_i4_7, ILOpCode.Ldc_i4_8, ILOpCode.Ldc_i4_m1,
        ILOpCode.Ldstr, ILOpCode.Br, ILOpCode.Br_s, ILOpCode.Brtrue, ILOpCode.Brtrue_s,
        ILOpCode.Brfalse, ILOpCode.Brfalse_s, ILOpCode.Beq, ILOpCode.Beq_s, ILOpCode.Bne_un, ILOpCode.Bne_un_s,
        ILOpCode.Blt, ILOpCode.Blt_s, ILOpCode.Bgt, ILOpCode.Bgt_s, ILOpCode.Ble, ILOpCode.Ble_s,
        ILOpCode.Bge, ILOpCode.Bge_s, ILOpCode.Add, ILOpCode.Sub, ILOpCode.Mul, ILOpCode.Div,
        ILOpCode.Rem, ILOpCode.Neg, ILOpCode.And, ILOpCode.Or, ILOpCode.Xor, ILOpCode.Not,
        ILOpCode.Ceq, ILOpCode.Clt, ILOpCode.Cgt, ILOpCode.Call, ILOpCode.Ret, ILOpCode.Pop, ILOpCode.Dup
    ];

    private static readonly HashSet<ILOpCode> Forbidden = [
        ILOpCode.Calli, ILOpCode.Jmp, ILOpCode.Localloc, ILOpCode.Cpblk, ILOpCode.Initblk,
        ILOpCode.Ldftn, ILOpCode.Ldvirtftn, ILOpCode.Ldtoken, ILOpCode.Mkrefany,
        ILOpCode.Refanytype, ILOpCode.Refanyval, ILOpCode.Arglist, ILOpCode.Throw,
        ILOpCode.Rethrow, ILOpCode.Box, ILOpCode.Unbox, ILOpCode.Unbox_any,
        ILOpCode.Castclass, ILOpCode.Isinst, ILOpCode.Ldsfld, ILOpCode.Stsfld
    ];

    public static void VerifyBody(
        MetadataReader reader,
        VerificationPolicy policy,
        MethodBodyBlock body,
        List<VerificationDiagnostic> diagnostics)
    {
        if (body.ExceptionRegions.Any()) {
            diagnostics.Add(new VerificationDiagnostic("V-EXCEPTION", "exception handlers are not allowed"));
        }

        var il = body.GetILReader();
        while (il.RemainingBytes > 0) {
            var opcode = ReadOpCode(ref il);
            if (Forbidden.Contains(opcode) || !Allowed.Contains(opcode)) {
                diagnostics.Add(new VerificationDiagnostic("V-OPCODE", $"opcode '{opcode}' is not allowed"));
            }

            VerifyOperand(reader, policy, opcode, ref il, diagnostics);
        }
    }

    private static ILOpCode ReadOpCode(ref BlobReader il)
    {
        var first = il.ReadByte();
        if (first != 0xFE) {
            return (ILOpCode)first;
        }

        return (ILOpCode)(0xFE00 | il.ReadByte());
    }

    private static void VerifyOperand(
        MetadataReader reader,
        VerificationPolicy policy,
        ILOpCode opcode,
        ref BlobReader il,
        List<VerificationDiagnostic> diagnostics)
    {
        if (opcode is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj) {
            var handle = MetadataTokens.EntityHandle(il.ReadInt32());
            var member = MetadataName.Member(reader, handle);
            if (!policy.IsMemberAllowed(member.TypeName, member.MemberName)) {
                diagnostics.Add(new VerificationDiagnostic("V-MEMBER", $"member '{member.TypeName}.{member.MemberName}' is not allowed"));
            }

            return;
        }

        SkipOperand(opcode, ref il);
    }

    private static void SkipOperand(ILOpCode opcode, ref BlobReader il)
    {
        if (opcode.IsBranch()) {
            if (opcode.GetBranchOperandSize() == 1) {
                _ = il.ReadSByte();
                return;
            }

            _ = il.ReadInt32();
            return;
        }

        switch (opcode) {
            case ILOpCode.Ldarg or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Stloc:
                _ = il.ReadUInt16();
                break;
            case ILOpCode.Ldarg_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s:
                _ = il.ReadByte();
                break;
            case ILOpCode.Ldc_i4 or ILOpCode.Ldstr:
                _ = il.ReadInt32();
                break;
            case ILOpCode.Switch:
                var count = il.ReadInt32();
                for (var i = 0; i < count; i++) {
                    _ = il.ReadInt32();
                }

                break;
        }
    }
}
