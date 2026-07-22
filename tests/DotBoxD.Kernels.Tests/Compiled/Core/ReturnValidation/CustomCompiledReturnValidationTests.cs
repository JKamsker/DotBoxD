using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ReturnValidation;

public sealed class CustomCompiledReturnValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Current_custom_artifact_with_deeply_malformed_return_fails_closed(
        bool suppressSuccessfulAudit)
    {
        using var host = HostWithMalformedCompiler();
        var plan = await PrepareListPlanAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            Options(suppressSuccessfulAudit));

        AssertFailure(result);
    }

    [Fact]
    public async Task Prepared_value_runner_rejects_current_custom_artifact_with_deeply_malformed_return()
    {
        using var host = HostWithMalformedCompiler();
        var plan = await PrepareListPlanAsync(host);

        var result = await host.ExecutePreparedValueInProcessAsync(
            plan,
            "main",
            SandboxValue.Unit,
            Options(suppressSuccessfulAudit: true));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("function return type mismatch", result.Error.SafeMessage);
        Assert.NotNull(result.ArtifactHash);
        Assert.NotNull(result.FullResult);
        AssertFailure(result.FullResult);
    }

    [Fact]
    public async Task Current_custom_artifact_cannot_forge_proof_before_mutating_owned_return()
    {
        using var host = HostWithForgedProofCompiler();
        var plan = await PrepareListPlanAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            Options(suppressSuccessfulAudit: false));

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.VerifierFailure, result.Error!.Code);
        Assert.Equal("compiled artifact failed verification", result.Error.SafeMessage);
    }

    [Fact]
    public async Task Verifier_rejects_proof_publication_with_mutation_before_return()
    {
        var assembly = CompiledArtifactTestFactory.BuildForgedI32ListReturnValidationProofAssembly(
            parameterCount: 0);

        var result = await VerifierTestHelpers.VerifyAsync(assembly);

        Assert.False(result.Succeeded);
        var diagnostic = Assert.Single(
            result.Diagnostics,
            item => item.Code == "V-COMPILED-SHAPE" &&
                    item.Message.Contains(
                        "may only publish return validation immediately before ExitCall and return",
                        StringComparison.Ordinal));
        Assert.NotNull(diagnostic);
    }

    [Fact]
    public async Task Current_custom_artifact_cannot_publish_proof_from_nested_function()
    {
        using var host = HostWithCompiler(CompiledReturnValidationAttackAssemblyFactory.BuildNestedPublisherAssembly());
        var plan = await PrepareListPlanAsync(host);

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            Options(suppressSuccessfulAudit: false));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.VerifierFailure, result.Error!.Code);
    }

    [Theory]
    [MemberData(nameof(NestedProofAttacks))]
    public async Task Verifier_rejects_nested_or_recursive_proof_publication(
        byte[] assembly,
        string expectedMessage)
    {
        var result = await VerifierTestHelpers.VerifyAsync(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Diagnostics,
            item => item.Code == "V-COMPILED-SHAPE" &&
                    item.Message.Contains(expectedMessage, StringComparison.Ordinal));
    }

    public static TheoryData<byte[], string> NestedProofAttacks()
        => new()
        {
            {
                CompiledReturnValidationAttackAssemblyFactory.BuildNestedPublisherAssembly(),
                "may not publish entrypoint return validation"
            },
            {
                CompiledReturnValidationAttackAssemblyFactory.BuildRecursivePublisherAssembly(),
                "must publish return validation on every reachable return"
            },
            {
                CompiledReturnValidationAttackAssemblyFactory.BuildUnbalancedInlineDepthAssembly(),
                "must balance regular and inline call depth on every path"
            },
            {
                CompiledReturnValidationAttackAssemblyFactory.BuildBranchIntoUnreachablePublicationSuffixAssembly(),
                "must publish return validation on every reachable return"
            }
        };

    [Fact]
    public async Task Verifier_rejects_duplicate_canonical_entrypoint_names()
    {
        var assembly = CompiledReturnValidationAttackAssemblyFactory.BuildDuplicateEntrypointNameAssembly();

        var result = await VerifierTestHelpers.VerifyAsync(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Diagnostics,
            item => item.Code == "V-METHOD-NAME" &&
                    item.Message.Contains("Fn_0", StringComparison.Ordinal) &&
                    item.Message.Contains("must be unique", StringComparison.Ordinal));
    }

    private static SandboxHost HostWithMalformedCompiler()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new MalformedListCompiler());
        });

    private static SandboxHost HostWithForgedProofCompiler()
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new ForgedProofCompiler());
        });

    private static SandboxHost HostWithCompiler(byte[] assembly)
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new AssemblyCompiler(assembly));
        });

    private static async Task<ExecutionPlan> PrepareListPlanAsync(SandboxHost host)
    {
        var module = await host.ImportJsonAsync("""
        {
          "id": "custom-compiled-return-validation",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": { "name": "List", "arguments": ["I32"] },
            "body": [{
              "op": "return",
              "value": { "call": "list.of", "args": [{ "i32": 1 }] }
            }]
          }]
        }
        """);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
    }

    private static SandboxExecutionOptions Options(bool suppressSuccessfulAudit)
        => new()
        {
            Mode = ExecutionMode.Compiled,
            AllowFallbackToInterpreter = false,
            SuppressSuccessfulRunSummaryAudit = suppressSuccessfulAudit
        };

    private static void AssertFailure(SandboxExecutionResult result)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        Assert.Equal("function return type mismatch", result.Error.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.NotNull(result.ArtifactHash);
    }

    private sealed class MalformedListCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildMalformedI32ListAssembly(parameterCount: 0)));
    }

    private sealed class ForgedProofCompiler : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(
                plan,
                CompiledArtifactTestFactory.BuildForgedI32ListReturnValidationProofAssembly(
                    parameterCount: 0)));
    }

    private sealed class AssemblyCompiler(byte[] assembly) : ISandboxCompiler
    {
        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(CompiledArtifactTestFactory.LoadedAssembly(plan, assembly));
    }
}
