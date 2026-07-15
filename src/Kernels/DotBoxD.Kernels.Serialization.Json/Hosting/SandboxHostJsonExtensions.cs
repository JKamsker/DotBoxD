using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Serialization.Json.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(jsonIr);
        host.ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(JsonImporter.Import(jsonIr));
    }
}
