using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace DotBoxD.Plugins.Fody;

internal sealed class InvokeAsyncClosureMap
{
    private readonly Dictionary<string, TypeDefinition?> _closures = new(StringComparer.Ordinal);

    public bool TryGetClosure(MethodDefinition interceptor, out TypeDefinition closure)
    {
        if (_closures.TryGetValue(interceptor.FullName, out var candidate) &&
            candidate is not null)
        {
            closure = candidate;
            return true;
        }

        closure = null!;
        return false;
    }

    public static InvokeAsyncClosureMap Discover(ModuleDefinition module, TypeDefinition generatedType)
    {
        var map = new InvokeAsyncClosureMap();
        foreach (var method in module.AllMethods())
        {
            if (!method.HasBody)
            {
                continue;
            }

            var instructions = method.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (!TryGetInterceptorCall(instructions[i], generatedType, out var interceptor))
                {
                    continue;
                }

                map.Add(interceptor, TryGetClosureType(instructions, i));
            }
        }

        return map;
    }

    private void Add(MethodReference interceptor, TypeDefinition? closure)
    {
        if (!_closures.TryGetValue(interceptor.FullName, out var existing))
        {
            _closures.Add(interceptor.FullName, closure);
            return;
        }

        if (existing is null ||
            closure is null ||
            !string.Equals(existing.FullName, closure.FullName, StringComparison.Ordinal))
        {
            _closures[interceptor.FullName] = null;
        }
    }

    private static bool TryGetInterceptorCall(
        Instruction instruction,
        TypeDefinition generatedType,
        out MethodReference interceptor)
    {
        if (instruction.Operand is MethodReference
            {
                DeclaringType.FullName: var declaringType,
                Name: var methodName
            } method &&
            string.Equals(declaringType, generatedType.FullName, StringComparison.Ordinal) &&
            methodName.StartsWith(DotBoxDInvokeAsyncWeaverNames.InvokeAsyncMethodPrefix, StringComparison.Ordinal))
        {
            interceptor = method;
            return true;
        }

        interceptor = null!;
        return false;
    }

    private static TypeDefinition? TryGetClosureType(
        Collection<Instruction> instructions,
        int callIndex)
    {
        if (callIndex < 2 ||
            instructions[callIndex - 1].OpCode.Code != Code.Newobj ||
            instructions[callIndex - 2].OpCode.Code is not (Code.Ldftn or Code.Ldvirtftn) ||
            instructions[callIndex - 2].Operand is not MethodReference closureMethod)
        {
            return null;
        }

        return closureMethod.DeclaringType.Resolve();
    }
}
