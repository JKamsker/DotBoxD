namespace DotBoxD.Kernels.Tests.Samples.GameServer;

public sealed class GamePluginControlServiceSourceTests
{
    [Fact]
    public void InvokeServerExtensionAsync_uses_owner_checked_kernel_snapshot()
    {
        var source = File.ReadAllText(GamePluginServerExtensionInvokerPath());
        var method = ExtractInvokeAsync(source);

        Assert.DoesNotContain("_server.Kernels.Get(pluginId)", method, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(kernel.OwnerId, _session)", method, StringComparison.Ordinal);
    }

    private static string ExtractInvokeAsync(string source)
    {
        const string startMarker = "public async ValueTask<byte[]> InvokeAsync";
        const string endMarker = "private static SandboxFunction RpcEntrypoint";
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);

        Assert.True(start >= 0, "InvokeAsync method was not found.");
        Assert.True(end > start, "RpcEntrypoint method marker was not found.");
        return source[start..end];
    }

    private static string GamePluginServerExtensionInvokerPath()
        => Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "samples",
            "GameServer",
            "Examples.GameServer.Server",
            "Ipc",
            "GamePluginServerExtensionInvoker.cs"));
}
