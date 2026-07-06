using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

internal static class CompiledArtifactArgumentValidationFixtures
{
    public static CompiledArtifact ValidDynamicArtifact()
        => CompiledArtifactTestFactory.DynamicMethod(Plan(), (_, _) => SandboxValue.Unit);

    public static ExecutionPlan Plan()
        => new(
            "module-hash",
            "plan-hash",
            new ExecutionPlanSeal("seal"),
            "policy-hash",
            "binding-hash",
            EmptyModule(),
            SandboxPolicyBuilder.Create().Build(),
            new BindingRegistryBuilder().Build(),
            new ResourceLimits(),
            new Dictionary<string, FunctionAnalysis>
            {
                ["main"] = new(SandboxType.I32, SandboxEffect.Cpu, true)
            },
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
            {
                ["main"] = new HashSet<string>(StringComparer.Ordinal)
            });

    public static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    public static SandboxFunction EmptyFunction()
        => new(
            "main",
            true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(0), new SourceSpan(1, 1)), new SourceSpan(1, 1))]);
}
