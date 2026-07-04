using System.Net;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Runtime.GrantReader;

public sealed class RuntimeGrantReaderCsvParityTests
{
    [Fact]
    public async Task SafeHttpClient_rejects_trailing_empty_allowed_hosts_entries()
    {
        var context = CreateContext(
            "malformed-http-grant",
            SandboxEffects.Pure | SandboxEffect.Network,
            [
                new CapabilityGrant(
                    "net.http.get",
                    new Dictionary<string, string>
                    {
                        ["allowedHosts"] = "api.example.com,",
                        ["maxResponseBytes"] = "1024"
                    })
            ],
            new ResourceLimits(
                MaxFuel: 5_000,
                MaxNetworkBytesRead: 1024,
                MaxNetworkBytesWritten: 1024));
        DotBoxD.Hosting.Http.SafeDnsResolver dns = (_, _) => ValueTask.FromResult<IReadOnlyList<IPAddress>>(
            [IPAddress.Parse("93.184.216.34")]);

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await DotBoxD.Hosting.Http.SafeHttpClient.GetTextAsync(
                context,
                new SandboxUri("https://api.example.com/config"),
                new DotBoxD.Hosting.Http.SafeInMemoryHttpMessageInvoker("ok"),
                dns,
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    [Fact]
    public async Task SafeFileSystem_rejects_trailing_empty_allowed_extension_entries()
    {
        using var temp = TempDirectory.Create();
        await System.IO.File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.txt"), "file-ok");
        var context = CreateContext(
            "malformed-file-grant",
            SandboxEffects.Pure | SandboxEffect.FileRead,
            [
                new CapabilityGrant(
                    "file.read",
                    new Dictionary<string, string>
                    {
                        ["root"] = temp.Path,
                        ["maxBytesPerRun"] = "1024",
                        ["allowedExtensions"] = ".txt,"
                    })
            ],
            new ResourceLimits(MaxFuel: 5_000, MaxFileBytesRead: 1024));

        var ex = await Assert.ThrowsAsync<SandboxRuntimeException>(async () =>
            await SafeFileSystem.ReadTextAsync(
                context,
                new SandboxPath("settings.txt"),
                CancellationToken.None));

        Assert.Equal(SandboxErrorCode.PermissionDenied, ex.Error.Code);
    }

    private static SandboxContext CreateContext(
        string policyId,
        SandboxEffect allowedEffects,
        IReadOnlyList<CapabilityGrant> grants,
        ResourceLimits limits)
    {
        var policy = new SandboxPolicy(policyId, allowedEffects, grants, limits);
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-" + Guid.NewGuid().ToString("N"));
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
