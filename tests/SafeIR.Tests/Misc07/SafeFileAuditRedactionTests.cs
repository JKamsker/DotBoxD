using SafeIR;

namespace SafeIR.Tests;

public sealed class SafeFileAuditRedactionTests
{
    [Fact]
    public async Task File_read_failure_audit_redacts_invalid_programmatic_path()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "file-path-input",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "path", "type": "SandboxPath" }],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "file.readText", "args": [{ "var": "path" }] }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileRead(temp.Path, 1024)
                .WithFuel(5_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            new SandboxPathValue(new SandboxPath("../secret.txt")));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.PermissionDenied, result.Error!.Code);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && !e.Success);
        Assert.Equal("sandbox://file.read/[invalid-path]", audit.ResourceId);
        Assert.DoesNotContain("secret", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", audit.ResourceId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task File_read_failure_audit_redacts_secret_shaped_valid_path()
    {
        using var temp = TempDirectory.Create();
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "file-secret-path-input",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [{ "name": "path", "type": "SandboxPath" }],
              "returnType": "String",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "file.readText", "args": [{ "var": "path" }] }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create()
                .GrantFileRead(temp.Path, 1024)
                .WithFuel(5_000)
                .Build());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            new SandboxPathValue(new SandboxPath("profiles/token-abc123.json")));

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.NotFound, result.Error!.Code);
        var audit = Assert.Single(result.AuditEvents, e => e.BindingId == "file.readText" && !e.Success);
        Assert.DoesNotContain("abc123", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", audit.ResourceId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "safe-ir-file-audit-" + Guid.NewGuid().ToString("N"));
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
