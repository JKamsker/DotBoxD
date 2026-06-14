namespace DotBoxD.Kernels.Serialization.Json.Internal;

using DotBoxD.Hosting;

public static class SandboxHostJsonExtensions
{
    public static ValueTask<SandboxModule> ImportJsonAsync(
        this SandboxHost host,
        string jsonIr,
        CancellationToken cancellationToken = default)
        => DotBoxD.Kernels.Serialization.Json.SandboxHostJsonExtensions.ImportJsonAsync(
            host,
            jsonIr,
            cancellationToken);
}
