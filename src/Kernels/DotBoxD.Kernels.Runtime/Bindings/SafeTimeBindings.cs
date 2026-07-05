using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class SafeTimeBindings
{
    public static BindingDescriptor NowUnixMillis { get; } = new(
        SafeTimeBindingNames.NowUnixMillisId,
        SemVersion.One,
        [],
        SandboxType.I64,
        SandboxEffect.Cpu | SandboxEffect.Time,
        "time.now",
        BindingCostModel.Fixed(2),
        AuditLevel.PerCall,
        BindingSafety.ReadOnlyExternal,
        (context, _, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = DateTimeOffset.UtcNow;
            var timestamp = context.UtcNow();
            var value = timestamp.ToUnixTimeMilliseconds();
            var fields = new Dictionary<string, string>(
                context.BindingAuditFields("clock", startedAt),
                StringComparer.Ordinal)
            {
                [SafeTimeBindingNames.NowUnixMillisAuditField] =
                    value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            };
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                timestamp,
                true,
                BindingId: SafeTimeBindingNames.NowUnixMillisId,
                CapabilityId: "time.now",
                Effect: SandboxEffect.Time,
                ResourceId: "clock:utc",
                Fields: fields));
            return ValueTask.FromResult(SandboxValue.FromInt64(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
