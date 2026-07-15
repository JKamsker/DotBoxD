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

                map.Add(interceptor, TryGetClosureType(instructions, i, interceptor));
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
        int callIndex,
        MethodReference interceptor)
    {
        var lambdaType = interceptor.Resolve()
            ?.Parameters
            .FirstOrDefault(static parameter =>
                string.Equals(
                    parameter.Name,
                    DotBoxDInvokeAsyncWeaverNames.LambdaParameterName,
                    StringComparison.Ordinal))
            ?.ParameterType;
        if (lambdaType is null)
        {
            return null;
        }

        const int maximumArgumentInstructionCount = 64;
        var firstCandidate = Math.Max(1, callIndex - maximumArgumentInstructionCount);
        for (var i = callIndex - 1; i >= firstCandidate; i--)
        {
            if (instructions[i].OpCode.Code == Code.Newobj &&
                instructions[i].Operand is MethodReference constructor &&
                string.Equals(constructor.DeclaringType.FullName, lambdaType.FullName, StringComparison.Ordinal) &&
                instructions[i - 1].OpCode.Code is Code.Ldftn or Code.Ldvirtftn &&
                instructions[i - 1].Operand is MethodReference closureMethod)
            {
                return closureMethod.DeclaringType.Resolve();
            }
        }

        return null;
    }
}
