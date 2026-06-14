namespace DotBoxD.Kernels.Serialization.Json;

using DotBoxD.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(JsonImporter.Import(jsonIr));
    }
}
