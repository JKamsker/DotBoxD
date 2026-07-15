using DotBoxD.DebugAdapter.Diagnostics;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class AdapterDiagnosticsTests
{
    [Fact]
    public async Task Concurrent_writes_are_not_silently_dropped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dotboxd-adapter-{Guid.NewGuid():N}.log");
        AdapterDiagnostics.Configure(path);
        try
        {
            using var start = new ManualResetEventSlim();
            var writes = Enumerable.Range(0, 200)
                .Select(index => Task.Run(() =>
                {
                    start.Wait();
                    AdapterDiagnostics.Write($"message {index}");
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(writes);

            Assert.Equal(200, File.ReadAllLines(path).Length);
        }
        finally
        {
            AdapterDiagnostics.Configure(string.Empty);
            File.Delete(path);
        }
    }
}
