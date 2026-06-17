using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace DotBoxD.Plugins.Fody;

internal static class InvokeAsyncClosureCaptureWeaver
{
    public static InvokeAsyncClosureCaptureWeaveResult Rewrite(
        ModuleDefinition module,
        Action<string> info,
        Action<string> warning)
    {
        var generatedType = module.GetType(DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName);
        if (generatedType is null)
        {
            return default;
        }

        var closureMap = InvokeAsyncClosureMap.Discover(module, generatedType);
        var targetGetter = module.ImportReference(
            typeof(Delegate).GetProperty(nameof(Delegate.Target))!.GetMethod);
        var rewrittenInterceptors = 0;
        var rewrittenAccesses = 0;

        foreach (var interceptor in generatedType.Methods.Where(IsInvokeAsyncInterceptor))
        {
            var rewritten = RewriteInterceptor(interceptor, closureMap, targetGetter, info, warning);
            if (rewritten == 0)
            {
                continue;
            }

            rewrittenInterceptors++;
            rewrittenAccesses += rewritten;
        }

        return new InvokeAsyncClosureCaptureWeaveResult(rewrittenInterceptors, rewrittenAccesses);
    }

    private static int RewriteInterceptor(
        MethodDefinition interceptor,
        InvokeAsyncClosureMap closureMap,
        MethodReference targetGetter,
        Action<string> info,
        Action<string> warning)
    {
        var moveNext = interceptor.GetAsyncStateMachineType()
            ?.Methods
            .FirstOrDefault(static method => method.Name == DotBoxDInvokeAsyncWeaverNames.MoveNextMethodName);

        if (moveNext is null ||
            !moveNext.HasBody ||
            !ContainsCaptureHelperCall(moveNext))
        {
            return 0;
        }

        if (!closureMap.TryGetClosure(interceptor, out var closure))
        {
            info($"DotBoxD InvokeAsync weaver kept reflection fallback for {interceptor.Name}: closure type was not provable.");
            return 0;
        }

        if (!CanExposeClosure(closure))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for {interceptor.Name}: closure type is not safely accessible.");
            return 0;
        }

        MakeClosureAssemblyVisible(closure);
        return RewriteMoveNext(moveNext, closure, targetGetter, warning);
    }

    private static int RewriteMoveNext(
        MethodDefinition moveNext,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        var rewritten = 0;
        var instructions = moveNext.Body.Instructions;
        for (var i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];
            if (IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName) &&
                TryRewriteRead(instructions, i, moveNext.Module, closure, targetGetter, warning))
            {
                rewritten++;
                continue;
            }

            if (IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName) &&
                TryRewriteWrite(instructions, i, moveNext.Module, closure, targetGetter, warning))
            {
                rewritten++;
            }
        }

        return rewritten;
    }

    private static bool TryRewriteRead(
        Collection<Instruction> instructions,
        int callIndex,
        ModuleDefinition module,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        var fieldNameIndex = callIndex - 1;
        if (!TryGetCaptureField(instructions, fieldNameIndex, closure, out var field))
        {
            return false;
        }

        if (!FieldMatchesCaptureType(instructions[callIndex], field))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for capture '{field.Name}': field type changed.");
            return false;
        }

        instructions[callIndex - 1] = Instruction.Create(OpCodes.Callvirt, targetGetter);
        instructions[callIndex] = Instruction.Create(OpCodes.Castclass, module.ImportReference(closure));
        instructions.Insert(callIndex + 1, Instruction.Create(OpCodes.Ldfld, module.ImportReference(field)));
        return true;
    }

    private static bool TryRewriteWrite(
        Collection<Instruction> instructions,
        int callIndex,
        ModuleDefinition module,
        TypeDefinition closure,
        MethodReference targetGetter,
        Action<string> warning)
    {
        if (!TryFindWriteFieldName(instructions, callIndex, closure, out var fieldNameIndex, out var field))
        {
            return false;
        }

        if (!FieldMatchesCaptureType(instructions[callIndex], field))
        {
            warning($"DotBoxD InvokeAsync weaver kept reflection fallback for capture '{field.Name}': field type changed.");
            return false;
        }

        instructions[fieldNameIndex] = Instruction.Create(OpCodes.Callvirt, targetGetter);
        instructions.Insert(fieldNameIndex + 1, Instruction.Create(OpCodes.Castclass, module.ImportReference(closure)));
        instructions[callIndex + 1] = Instruction.Create(OpCodes.Stfld, module.ImportReference(field));
        return true;
    }

    private static bool TryGetCaptureField(
        Collection<Instruction> instructions,
        int fieldNameIndex,
        TypeDefinition closure,
        out FieldDefinition field)
    {
        if (fieldNameIndex < 2 ||
            instructions[fieldNameIndex - 2].OpCode.Code != Code.Ldarg_0 ||
            instructions[fieldNameIndex - 1].OpCode.Code != Code.Ldfld ||
            instructions[fieldNameIndex - 1].Operand is not FieldReference lambdaField ||
            !string.Equals(lambdaField.Name, DotBoxDInvokeAsyncWeaverNames.LambdaParameterName, StringComparison.Ordinal) ||
            instructions[fieldNameIndex] is not { OpCode.Code: Code.Ldstr, Operand: string fieldName })
        {
            field = null!;
            return false;
        }

        field = closure.Fields.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, fieldName, StringComparison.Ordinal))!;
        return field is not null && IsFieldAccessible(field);
    }

    private static bool TryFindWriteFieldName(
        Collection<Instruction> instructions,
        int callIndex,
        TypeDefinition closure,
        out int fieldNameIndex,
        out FieldDefinition field)
    {
        for (var i = callIndex - 1; i >= 2; i--)
        {
            if (TryGetCaptureField(instructions, i, closure, out field))
            {
                fieldNameIndex = i;
                return true;
            }
        }

        fieldNameIndex = -1;
        field = null!;
        return false;
    }

    private static bool IsInvokeAsyncInterceptor(MethodDefinition method)
        => method.Name.StartsWith(DotBoxDInvokeAsyncWeaverNames.InvokeAsyncMethodPrefix, StringComparison.Ordinal);

    private static bool ContainsCaptureHelperCall(MethodDefinition method)
        => method.Body.Instructions.Any(static instruction =>
            IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName) ||
            IsCaptureHelperCall(instruction, DotBoxDInvokeAsyncWeaverNames.WriteCaptureMethodName));

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

    private static bool CanExposeClosure(TypeDefinition closure)
        => IsCompilerGenerated(closure) && IsParentChainAccessibleFromSameAssembly(closure.DeclaringType);

    private static bool IsCompilerGenerated(TypeDefinition type)
        => type.CustomAttributes.Any(static attribute =>
            string.Equals(
                attribute.AttributeType.FullName,
                DotBoxDInvokeAsyncWeaverNames.CompilerGeneratedAttribute,
                StringComparison.Ordinal));

    private static bool IsParentChainAccessibleFromSameAssembly(TypeDefinition? type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.IsNestedPrivate || current.IsNestedFamily || current.IsNestedFamilyAndAssembly)
            {
                return false;
            }
        }

        return true;
    }

    private static void MakeClosureAssemblyVisible(TypeDefinition closure)
    {
        if (!closure.IsNestedPrivate)
        {
            return;
        }

        closure.Attributes &= ~TypeAttributes.VisibilityMask;
        closure.Attributes |= TypeAttributes.NestedAssembly;
    }
}

internal readonly struct InvokeAsyncClosureCaptureWeaveResult
{
    public InvokeAsyncClosureCaptureWeaveResult(int rewrittenInterceptors, int rewrittenAccesses)
    {
        RewrittenInterceptors = rewrittenInterceptors;
        RewrittenAccesses = rewrittenAccesses;
    }

    public int RewrittenInterceptors { get; }

    public int RewrittenAccesses { get; }
}
