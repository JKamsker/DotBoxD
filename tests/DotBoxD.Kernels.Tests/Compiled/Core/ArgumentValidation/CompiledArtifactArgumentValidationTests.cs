using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

public sealed class CompiledArtifactArgumentValidationTests
{
    [Fact]
    public void Compiled_artifact_init_rejects_null_manifest()
    {
        var artifact = ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Manifest = null! });

        Assert.Equal(nameof(CompiledArtifact.Manifest), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_verification()
    {
        var artifact = ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Verification = null! });

        Assert.Equal(nameof(CompiledArtifact.Verification), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_assembly_hash()
    {
        var artifact = ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { AssemblyHash = null! });

        Assert.Equal(nameof(CompiledArtifact.AssemblyHash), ex.ParamName);
    }

    [Fact]
    public void Compiled_artifact_init_rejects_null_entrypoint()
    {
        var artifact = ValidDynamicArtifact();

        var ex = Assert.Throws<ArgumentNullException>(() => artifact with { Entrypoint = null! });

        Assert.Equal(nameof(CompiledArtifact.Entrypoint), ex.ParamName);
    }

    private static CompiledArtifact ValidDynamicArtifact()
        => CompiledArtifactTestFactory.DynamicMethod(Plan(), (_, _) => SandboxValue.Unit);

    private static ExecutionPlan Plan()
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

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new(
            "main",
            true,
            [],
            SandboxType.I32,
            [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(0), new SourceSpan(1, 1)), new SourceSpan(1, 1))]);
}
