using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Policy.Surprise;

public sealed class ResourceLimitRangeDiagnosticTests
{
    [Fact]
    public async Task Prepare_reports_supported_wall_time_range_for_positive_limit_above_timer_maximum()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync(SandboxTestHost.PureScoreJson());
        var policy = new SandboxPolicy(
            "wall-time-too-large",
            SandboxEffects.Pure,
            [],
            new ResourceLimits(MaxWallTime: TimeSpan.MaxValue));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

        var diagnostic = Assert.Single(ex.Diagnostics, d => d.Code == "E-POLICY-LIMIT");
        Assert.Contains(nameof(ResourceLimits.MaxWallTime), diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("non-negative", diagnostic.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            diagnostic.Message.Contains("supported", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Message.Contains("maximum", StringComparison.OrdinalIgnoreCase) ||
            diagnostic.Message.Contains("range", StringComparison.OrdinalIgnoreCase),
            $"Expected diagnostic to explain the supported wall-time range, but was: {diagnostic.Message}");
    }
}
