using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace DotBoxD.Plugins.Fody;

internal static class InvokeAsyncCaptureAccessRewriter
{
    public static int Rewrite(
        MethodDefinition method,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        var rewritten = 0;
        var instructions = method.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName) &&
                TryRewriteRead(instructions, i, method, closure, targetGetter, warning))
            {
                rewritten++;
                continue;
            }

            if (IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName) &&
                TryRewriteWrite(instructions, i, method, closure, targetGetter, warning))
            {
                rewritten++;
            }
        }

        return rewritten;
    }

    public static bool ContainsCaptureHelperCall(MethodDefinition method)
        => method.Body.Instructions.Any(static instruction =>
            IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName) ||
            IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName));

    private static bool TryRewriteRead(
        Collection<Instruction> instructions,
        int callIndex,
        MethodDefinition method,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        var fieldNameIndex = callIndex - 1;
        if (!TryGetCaptureField(instructions, fieldNameIndex, method, closure, out var field))
        {
            return false;
        }

        if (!FieldMatchesCaptureType(instructions[callIndex], field))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for capture '{field.Name}': field type changed.");
            return false;
        }

        instructions[fieldNameIndex] = Instruction.Create(OpCodes.Callvirt, targetGetter);
        instructions[callIndex] = Instruction.Create(OpCodes.Castclass, method.Module.ImportReference(closure));
        instructions.Insert(callIndex + 1, Instruction.Create(OpCodes.Ldfld, method.Module.ImportReference(field)));
        return true;
    }

    private static bool TryRewriteWrite(
        Collection<Instruction> instructions,
        int callIndex,
        MethodDefinition method,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        if (!TryFindWriteFieldName(instructions, callIndex, method, closure, out var fieldNameIndex, out var field))
        {
            return false;
        }

        if (!FieldMatchesCaptureType(instructions[callIndex], field))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for capture '{field.Name}': field type changed.");
            return false;
        }

        instructions[fieldNameIndex] = Instruction.Create(OpCodes.Callvirt, targetGetter);
        instructions.Insert(fieldNameIndex + 1, Instruction.Create(OpCodes.Castclass, method.Module.ImportReference(closure)));
        instructions[callIndex + 1] = Instruction.Create(OpCodes.Stfld, method.Module.ImportReference(field));
        return true;
    }

    private static bool TryGetCaptureField(
        Collection<Instruction> instructions,
        int fieldNameIndex,
        MethodDefinition method,
        TypeDefinition closure,
        out FieldDefinition field)
    {
        if (fieldNameIndex < 1 ||
            !HasDelegateSource(instructions, fieldNameIndex, method) ||
            instructions[fieldNameIndex] is not { OpCode.Code: Code.Ldstr, Operand: string fieldName })
        {
            field = null!;
            return false;
        }

        field = closure.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))!;
        return field is not null && IsFieldAccessible(field);
    }

    private static bool HasDelegateSource(
        Collection<Instruction> instructions,
        int fieldNameIndex,
        MethodDefinition method)
    {
        var source = instructions[fieldNameIndex - 1];
        if (IsLambdaArgumentLoad(source, method))
        {
            return true;
        }

        return fieldNameIndex >= 2 &&
               instructions[fieldNameIndex - 2].OpCode.Code == Code.Ldarg_0 &&
               source.OpCode.Code == Code.Ldfld &&
               source.Operand is FieldReference field &&
               string.Equals(field.Name, DotBoxDInvokeAsyncWeaverNames.LambdaParameterName, StringComparison.Ordinal);
    }

    private static bool IsLambdaArgumentLoad(Instruction instruction, MethodDefinition method)
    {
        if (instruction.Operand is ParameterReference parameter)
        {
            return string.Equals(
                parameter.Name,
                DotBoxDInvokeAsyncWeaverNames.LambdaParameterName,
                StringComparison.Ordinal);
        }

        var argumentIndex = instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => 0,
            Code.Ldarg_1 => 1,
            Code.Ldarg_2 => 2,
            Code.Ldarg_3 => 3,
            _ => -1,
        };
        var parameterIndex = argumentIndex - (method.HasThis ? 1 : 0);
        return parameterIndex >= 0 &&
               parameterIndex < method.Parameters.Count &&
               string.Equals(
                   method.Parameters[parameterIndex].Name,
                   DotBoxDInvokeAsyncWeaverNames.LambdaParameterName,
                   StringComparison.Ordinal);
    }

    private static bool TryFindWriteFieldName(
        Collection<Instruction> instructions,
        int callIndex,
        MethodDefinition method,
        TypeDefinition closure,
        out int fieldNameIndex,
        out FieldDefinition field)
    {
        for (var i = callIndex - 1; i >= 1; i--)
        {
            if (TryGetCaptureField(instructions, i, method, closure, out field))
            {
                fieldNameIndex = i;
                return true;
            }
        }

        fieldNameIndex = -1;
        field = null!;
        return false;
    }

    private static bool IsCaptureHelperCall(Instruction instruction, string name)
        => instruction.OpCode.Code == Code.Call &&
           instruction.Operand is MethodReference method &&
           string.Equals(method.Name, name, StringComparison.Ordinal) &&
           string.Equals(
               method.DeclaringType.FullName,
               DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName,
               StringComparison.Ordinal);

    private static bool FieldMatchesCaptureType(Instruction call, FieldDefinition field)
        => call.Operand is not GenericInstanceMethod generic ||
           generic.GenericArguments.Count != 1 ||
           string.Equals(generic.GenericArguments[0].FullName, field.FieldType.FullName, StringComparison.Ordinal);

    private static bool IsFieldAccessible(FieldDefinition field)
        => field.IsPublic || field.IsAssembly || field.IsFamilyOrAssembly;
}
