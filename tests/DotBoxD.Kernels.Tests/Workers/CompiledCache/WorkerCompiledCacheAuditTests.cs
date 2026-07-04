using System.Text.Json;
using DotBoxD.Hosting;
using DotBoxD.Kernels.Compiler;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Verifier;
using DotBoxD.Kernels.Verifier.Generated;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Workers.CompiledCache;

public sealed class WorkerCompiledCacheAuditTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [Fact]
    public async Task Worker_process_accepts_reference_worker_cache_invalidated_audit()
    {
        using var cacheRoot = TempDirectory.Create();
        using var worker = new SandboxHostWorkerClient(() => WorkerHost(cacheRoot.Path));
        using var host = RequestingHost(worker);
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.FromSeconds(10))
            .Build());
        var input = SandboxValue.FromList([SandboxValue.FromInt32(2), SandboxValue.FromInt32(1)]);

        var seeded = await ExecuteCompiledWorkerAsync(host, plan, input);
        Assert.True(seeded.Succeeded, seeded.Error?.SafeMessage);
        Assert.True(File.Exists(Path.Combine(CacheEntry(cacheRoot.Path, plan), "manifest.json")));

        await ReplaceManifestAsync(
            CacheEntry(cacheRoot.Path, plan),
            manifest => manifest with { VerifierVersion = "stale-worker-verifier" });

        var result = await ExecuteCompiledWorkerAsync(host, plan, input);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var invalidated = Assert.Single(result.AuditEvents, e => e.Kind == "CacheInvalidated");
        Assert.False(invalidated.Success);
        Assert.Equal(SandboxErrorCode.CacheInvalid, invalidated.ErrorCode);
        Assert.Equal("cache:" + CacheKey(plan), invalidated.ResourceId);
        Assert.Equal(plan.PlanHash, invalidated.Fields!["planHash"]);
        var summary = Assert.Single(result.AuditEvents, e => e.Kind == "RunSummary");
        Assert.Equal("Recompiled", summary.Fields!["cacheStatus"]);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "WorkerIsolationFailed");
    }

    private static SandboxHost RequestingHost(ISandboxWorkerClient worker)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseWorkerClient(worker, SandboxWorkerProfile.HardenedOutOfProcess);
        });

    private static SandboxHost WorkerHost(string cachePath)
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.UseInterpreter();
            builder.UseCompilerCache(cachePath);
            builder.UseCompilerIfAvailable();
        });

    private static async ValueTask<SandboxExecutionResult> ExecuteCompiledWorkerAsync(
        SandboxHost host,
        ExecutionPlan plan,
        SandboxValue input)
        => await host.ExecuteAsync(
            plan,
            "main",
            input,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                Isolation = SandboxIsolation.WorkerProcess,
                AllowFallbackToInterpreter = false
            });

    private static string CacheEntry(string root, ExecutionPlan plan)
    {
        var key = CacheKey(plan);
        return Path.Combine(root, key[..2], key[2..4], key);
    }

    private static string CacheKey(ExecutionPlan plan)
        => CacheKeyBuilder.Build(plan, "main", VerificationPolicy.BoxedValueDefaults(), optimize: false);

    private static async Task ReplaceManifestAsync(
        string entryPath,
        Func<ArtifactManifest, ArtifactManifest> replace)
    {
        var path = Path.Combine(entryPath, "manifest.json");
        ArtifactManifest manifest;
        await using (var read = File.OpenRead(path))
        {
            manifest = await JsonSerializer.DeserializeAsync<ArtifactManifest>(read, JsonOptions) ??
                throw new JsonException("empty manifest");
        }

        await using var write = File.Create(path);
        await JsonSerializer.SerializeAsync(write, replace(manifest), JsonOptions);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotboxd-worker-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
