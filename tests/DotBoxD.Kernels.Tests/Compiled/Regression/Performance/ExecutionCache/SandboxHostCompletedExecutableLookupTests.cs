using System.Runtime.Loader;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.ExecutionCache;

public sealed class SandboxHostCompletedExecutableLookupTests
{
    private static readonly SandboxExecutionOptions SuppressedOptions = new()
    {
        Mode = ExecutionMode.Compiled,
        AllowFallbackToInterpreter = false,
        SuppressSuccessfulRunSummaryAudit = true
    };

    private static readonly SandboxExecutionOptions AuditedOptions = SuppressedOptions with
    {
        SuppressSuccessfulRunSummaryAudit = false
    };

    [Fact]
    public async Task Second_suppressed_failure_reports_a_materialization_hit()
    {
        using var host = CreateHost(new GeneratedAssemblyVerifier());
        var plan = await PrepareAsync(host, FailureModuleJson);

        var first = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, SuppressedOptions);
        var second = await host.ExecuteAsync(plan, "main", SandboxValue.Unit, SuppressedOptions);

        AssertFailure(first, expectedMaterializationStatus: "Miss");
        AssertFailure(second, expectedMaterializationStatus: "Hit");
    }

    [Fact]
    public async Task Churned_hot_entry_invalidates_with_both_lrus_and_preserves_bypass_semantics()
    {
        var verifier = new CountingVerifier();
        using var host = CreateHost(verifier);
        var plans = new ExecutionPlan[67];
        for (var i = 0; i < plans.Length; i++)
        {
            plans[i] = await PrepareAsync(host, SuccessModuleJson($"completed-hot-churn-{i}"));
        }

        AssertSuccess(await ExecuteAsync(host, plans[0], SuppressedOptions));
        AssertSuccess(await ExecuteAsync(host, plans[1], SuppressedOptions));
        AssertSuccess(await ExecuteAsync(host, plans[2], SuppressedOptions));
        AssertSuccess(await ExecuteAsync(host, plans[2], SuppressedOptions));
        for (var i = 3; i < plans.Length; i++)
        {
            AssertSuccess(await ExecuteAsync(host, plans[i], SuppressedOptions));
        }

        var audited = await ExecuteAsync(host, plans[2], AuditedOptions);
        AssertSuccess(audited);
        AssertMaterializationSummary(audited, "Miss");

        using var cancellation = new CancellationTokenSource();
        var cancelable = await host.ExecuteAsync(
            plans[2],
            "main",
            SandboxValue.Unit,
            AuditedOptions,
            cancellation.Token);
        AssertSuccess(cancelable);
        AssertHitSummary(cancelable);
        Assert.Equal(68, verifier.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Host_dispose_releases_default_executable_load_context(bool suppressAudit)
    {
        var before = AssemblyLoadContext.All.ToArray();
        var host = CreateHost(new GeneratedAssemblyVerifier());
        var plan = await PrepareAsync(
            host,
            SuccessModuleJson($"completed-disposal-{suppressAudit}"));
        var result = await ExecuteAsync(
            host,
            plan,
            suppressAudit ? SuppressedOptions : AuditedOptions);
        AssertSuccess(result);
        Assert.Equal(suppressAudit, result.AuditEvents.Count == 0);
        var expectedName = "DotBoxD.Kernels.Generated.Host." + result.ArtifactHash;
        var context = AssemblyLoadContext.All.Single(candidate =>
            !before.Contains(candidate) &&
            string.Equals(candidate.Name, expectedName, StringComparison.Ordinal));
        var weakContext = new WeakReference(context);

        context = null!;
        host.Dispose();
        await WaitForUnloadAsync(weakContext);

        Assert.False(weakContext.IsAlive);
    }

    private static SandboxHost CreateHost(IGeneratedAssemblyVerifier verifier)
        => SandboxHost.Create(builder =>
        {
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable(new ReflectionEmitSandboxCompiler(verifier));
        });

    private static async Task<ExecutionPlan> PrepareAsync(SandboxHost host, string json)
    {
        var module = await host.ImportJsonAsync(json);
        return await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(long.MaxValue).Build());
    }

    private static ValueTask<SandboxExecutionResult> ExecuteAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxExecutionOptions options)
        => host.ExecuteAsync(plan, "main", SandboxValue.Unit, options);

    private static void AssertSuccess(SandboxExecutionResult result)
    {
        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(7, Assert.IsType<I32Value>(result.Value).Value);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
    }

    private static void AssertFailure(SandboxExecutionResult result, string expectedMaterializationStatus)
    {
        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.InvalidInput, result.Error!.Code);
        var summary = Assert.Single(result.AuditEvents, audit => audit.Kind == "RunSummary");
        Assert.Equal(expectedMaterializationStatus, summary.Fields!["materializationStatus"]);
    }

    private static void AssertHitSummary(SandboxExecutionResult result)
        => AssertMaterializationSummary(result, "Hit");

    private static void AssertMaterializationSummary(
        SandboxExecutionResult result,
        string expectedStatus)
    {
        var summary = Assert.Single(result.AuditEvents, audit => audit.Kind == "RunSummary");
        Assert.Equal(expectedStatus, summary.Fields!["materializationStatus"]);
    }

    private static async Task WaitForUnloadAsync(WeakReference weakContext)
    {
        for (var i = 0; i < 20 && weakContext.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(25);
        }
    }

    private sealed class CountingVerifier : IGeneratedAssemblyVerifier
    {
        private readonly GeneratedAssemblyVerifier _inner = new();
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<VerificationResult> VerifyAsync(
            ReadOnlyMemory<byte> assemblyBytes,
            ArtifactManifest manifest,
            VerificationPolicy policy,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _inner.VerifyAsync(assemblyBytes, manifest, policy, cancellationToken);
        }
    }

    private const string FailureModuleJson = """
    {
      "id": "completed-hot-suppressed-failure",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": { "op": "div", "left": { "i32": 1 }, "right": { "i32": 0 } }
        }]
      }]
    }
    """;

    private static string SuccessModuleJson(string id)
        => $$"""
        {
          "id": "{{id}}",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "I32",
            "body": [{ "op": "return", "value": { "i32": 7 } }]
          }]
        }
        """;
}
