using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class SandboxHostCompiledEntrypointValidationTests
{
    [Fact]
    public async Task Compiled_execution_rejects_private_function_before_custom_compiler_invocation()
    {
        var compiler = new CountingThrowingCompiler();
        var scenario = await CompiledEntrypointScenario.CreateAsync(compiler);

        var result = await scenario.ExecuteAsync("helper");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.ValidationError, result.Error!.Code);
        Assert.Equal("entrypoint 'helper' is not available", result.Error.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.False(result.ExecutionDispatched);
        Assert.Equal(0, compiler.Calls);
    }

    [Fact]
    public async Task Compiled_execution_invokes_custom_compiler_for_public_entrypoint()
    {
        var compiler = new CountingThrowingCompiler();
        var scenario = await CompiledEntrypointScenario.CreateAsync(compiler);

        var result = await scenario.ExecuteAsync("main");

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, result.Error!.Code);
        Assert.Equal(1, compiler.Calls);
    }

    private sealed class CountingThrowingCompiler : ISandboxCompiler
    {
        public int Calls { get; private set; }

        public ValueTask<CompiledArtifact> CompileAsync(
            ExecutionPlan plan,
            CompileOptions options,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("compiler must not be called for private functions");
        }
    }

    private sealed record CompiledEntrypointScenario(SandboxHost Host, ExecutionPlan Plan)
    {
        public static async Task<CompiledEntrypointScenario> CreateAsync(ISandboxCompiler compiler)
        {
            var host = SandboxHost.Create(builder =>
            {
                builder.AddDefaultPureBindings();
                builder.UseInterpreter();
                builder.UseCompilerIfAvailable(compiler);
            });
            var module = await host.ImportJsonAsync("""
            {
              "id": "compiled-entrypoint-validation",
              "version": "1.0.0",
              "functions": [
                {
                  "id": "main",
                  "visibility": "entrypoint",
                  "parameters": [],
                  "returnType": "I32",
                  "body": [{ "op": "return", "value": { "i32": 1 } }]
                },
                {
                  "id": "helper",
                  "visibility": "private",
                  "parameters": [],
                  "returnType": "I32",
                  "body": [{ "op": "return", "value": { "i32": 2 } }]
                }
              ]
            }
            """);
            var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
            return new CompiledEntrypointScenario(host, plan);
        }

        public ValueTask<SandboxExecutionResult> ExecuteAsync(string entrypoint)
            => Host.ExecuteAsync(
                Plan,
                entrypoint,
                SandboxValue.Unit,
                new SandboxExecutionOptions
                {
                    Mode = ExecutionMode.Compiled,
                    AllowFallbackToInterpreter = false
                });
    }
}
