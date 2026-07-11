using CsCheck;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Fuzz;

public sealed class EffectSoundnessPropertyTests
{
    private static readonly Gen<EffectKind> EffectKinds = Gen.Int.Select(
        value => (EffectKind)((uint)value % Enum.GetValues<EffectKind>().Length));

    [Fact]
    public void Declared_effects_cover_observed_capability_and_resource_effects()
        => EffectKinds.Sample(
            kind => VerifyAsync(kind).GetAwaiter().GetResult(),
            seed: "0N0XIzNsQ0O2",
            iter: 50,
            threads: 1);

    private static async Task VerifyAsync(EffectKind kind)
    {
        using var temp = TempDirectory.Create();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "input.txt"), "input");
        var testCase = CreateCase(kind);
        using var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(testCase.Json);
        var plan = await host.PrepareAsync(module, Policy(temp.Path));
        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var audit = Assert.Single(
            result.AuditEvents,
            item => item.BindingId == testCase.BindingId && item.Success);
        Assert.Equal(testCase.ObservedEffect, audit.Effect & testCase.ObservedEffect);
        Assert.Equal(testCase.ObservedEffect, plan.FunctionAnalysis["main"].Effects & testCase.ObservedEffect);
        Assert.Equal(1, result.ResourceUsage.HostCalls);
        testCase.AssertResources(result.ResourceUsage);
    }

    private static EffectCase CreateCase(EffectKind kind)
        => kind switch
        {
            EffectKind.Time => new(
                "time.nowUnixMillis",
                SandboxEffect.Time,
                Module("time", "time.now", "I64", """{ "call": "time.nowUnixMillis", "args": [] }"""),
                static _ => { }),
            EffectKind.Random => new(
                "random.nextI32",
                SandboxEffect.Random,
                Module(
                    "random",
                    "random",
                    "I32",
                    """{ "call": "random.nextI32", "args": [{ "i32": 0 }, { "i32": 10 }] }"""),
                static _ => { }),
            EffectKind.Log => new(
                "log.info",
                SandboxEffect.Audit,
                Module("log", "log.write", "Unit", """{ "call": "log.info", "args": [{ "string": "ok" }] }"""),
                static usage => Assert.Equal(1, usage.LogEvents)),
            EffectKind.FileRead => new(
                "file.readText",
                SandboxEffect.FileRead,
                Module("file-read", "file.read", "String", """{ "call": "file.readText", "args": [{ "path": "input.txt" }] }"""),
                static usage => Assert.Equal(5, usage.FileBytesRead)),
            EffectKind.FileWrite => new(
                "file.writeText",
                SandboxEffect.FileWrite,
                Module(
                    "file-write",
                    "file.write",
                    "Unit",
                    """{ "call": "file.writeText", "args": [{ "path": "output.txt" }, { "string": "output" }] }"""),
                static usage => Assert.Equal(6, usage.FileBytesWritten)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static SandboxPolicy Policy(string root)
        => SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .GrantTimeNow()
            .GrantRandom()
            .GrantLogging()
            .GrantFileRead(root, maxBytesPerRun: 1_024)
            .GrantFileWrite(root, maxBytesPerRun: 1_024, allowCreate: true)
            .WithFuel(10_000)
            .Build();

    private static string Module(string id, string capability, string returnType, string expression)
        => $$"""
        {
          "id":"effect-{{id}}","version":"1.0.0","capabilityRequests":[{"id":"{{capability}}"}],
          "functions":[{"id":"main","visibility":"entrypoint","parameters":[],"returnType":"{{returnType}}",
          "body":[{"op":"return","value":{{expression}}}]}]
        }
        """;

    private enum EffectKind
    {
        Time,
        Random,
        Log,
        FileRead,
        FileWrite
    }

    private sealed record EffectCase(
        string BindingId,
        SandboxEffect ObservedEffect,
        string Json,
        Action<SandboxResourceUsage> AssertResources);

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-effects-" + Guid.NewGuid().ToString("N"));
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
