using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters;

internal static class CompiledIntrinsicBindingMatcher
{
    public static bool IsPureRuntimeStub(
        BindingSignature binding,
        string boxedMethod,
        SandboxType returnType,
        IReadOnlyList<SandboxType> parameters,
        bool requireUnboundedCost = false)
    {
        if (!IsRuntimeStub(binding, boxedMethod))
        {
            return false;
        }

        if (!ParametersMatch(binding.Parameters, parameters))
        {
            return false;
        }

        if (!binding.ReturnType.Equals(returnType))
        {
            return false;
        }

        return HasPureIntrinsicContract(binding, requireUnboundedCost);
    }

    private static bool IsRuntimeStub(BindingSignature binding, string boxedMethod)
        => binding.Compiled is { Kind: "RuntimeStub" } &&
           binding.Compiled.Type == typeof(CompiledRuntime).FullName &&
           binding.Compiled.Method == boxedMethod;

    private static bool ParametersMatch(
        IReadOnlyList<SandboxType> actual,
        IReadOnlyList<SandboxType> expected)
    {
        if (actual.Count != expected.Count)
        {
            return false;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!actual[i].Equals(expected[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasPureIntrinsicContract(BindingSignature binding, bool requireUnboundedCost)
    {
        if (binding.RequiredCapability is not null)
        {
            return false;
        }

        if (binding.Safety != BindingSafety.PureIntrinsic || binding.AuditLevel != AuditLevel.None)
        {
            return false;
        }

        if (requireUnboundedCost && binding.CostModel.MaxCallsPerRun is not null)
        {
            return false;
        }

        return (binding.Effects & ~(SandboxEffect.Cpu | SandboxEffect.Alloc)) == SandboxEffect.None;
    }
}
