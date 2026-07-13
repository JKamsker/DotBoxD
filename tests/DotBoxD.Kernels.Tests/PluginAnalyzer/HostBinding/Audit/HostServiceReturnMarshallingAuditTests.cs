using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceReturnMarshallingAuditTests
{
    private const string BindingId = "host.probe.nullString";
    private const string CapabilityId = "probe.read.null";
    private static readonly SourceSpan Span = new(1, 1);

    [Fact]
    public async Task Null_string_return_failure_does_not_publish_successful_binding_audit()
    {
        using var host = SandboxHost.Create(
            builder => builder.AddBindingsFrom<INullReturnProbeWorld>(new NullReturnProbeWorld()));
        var plan = await host.PrepareAsync(NullStringBindingModule(), NullStringPolicy());

        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.BindingFailure, result.Error!.Code);
        Assert.Contains(
            result.AuditEvents,
            e => e.Kind == "BindingCall" &&
                 e.BindingId == BindingId &&
                 !e.Success &&
                 e.ErrorCode == SandboxErrorCode.BindingFailure);
        Assert.DoesNotContain(
            result.AuditEvents,
            e => e.Kind == "BindingCall" && e.BindingId == BindingId && e.Success);
    }

    private interface INullReturnProbeWorld
    {
        [HostBinding(BindingId, CapabilityId, SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.HostStateRead)]
        string GetValue();
    }

    private sealed class NullReturnProbeWorld : INullReturnProbeWorld
    {
        public string GetValue() => null!;
    }

    private static SandboxPolicy NullStringPolicy()
        => SandboxPolicyBuilder.Create()
            .Grant(CapabilityId, new { }, SandboxEffect.HostStateRead)
            .WithFuel(1_000)
            .WithMaxHostCalls(10)
            .WithWallTime(TimeSpan.FromSeconds(1))
            .Build();

    private static SandboxModule NullStringBindingModule()
        => new(
            "null-string-host-service-binding-probe",
            SemVersion.One,
            SemVersion.One,
            [],
            [
                new SandboxFunction(
                    "main",
                    true,
                    [],
                    SandboxType.String,
                    [new ReturnStatement(new CallExpression(BindingId, [], null, Span), Span)])
            ],
            new Dictionary<string, string>(StringComparer.Ordinal));
}
