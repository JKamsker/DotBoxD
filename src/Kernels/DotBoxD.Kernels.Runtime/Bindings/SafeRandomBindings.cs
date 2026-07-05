using System.Globalization;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime.Bindings;

public static class SafeRandomBindings
{
    public static BindingDescriptor NextI32 { get; } = new(
        "random.nextI32",
        SemVersion.One,
        [SandboxType.I32, SandboxType.I32],
        SandboxType.I32,
        SandboxEffect.Cpu | SandboxEffect.Random,
        "random",
        BindingCostModel.Fixed(3),
        AuditLevel.PerCall,
        BindingSafety.ReadOnlyExternal,
        (context, args, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = DateTimeOffset.UtcNow;
            var min = ((I32Value)args[0]).Value;
            var max = ((I32Value)args[1]).Value;
            var value = context.NextRandomInt32(min, max);
            var timestamp = context.AuditTimestamp();
            context.Audit.Write(new SandboxAuditEvent(
                context.RunId,
                "BindingCall",
                timestamp,
                true,
                BindingId: "random.nextI32",
                CapabilityId: "random",
                Effect: SandboxEffect.Random,
                ResourceId: "random:i32",
                Fields: RandomAuditFields(context, startedAt, min, max, value)));
            return ValueTask.FromResult(SandboxValue.FromInt32(value));
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static IReadOnlyDictionary<string, string> RandomAuditFields(
        SandboxContext context,
        DateTimeOffset startedAt,
        int minInclusive,
        int maxExclusive,
        int value)
    {
        var fields = context.BindingAuditFields("random", startedAt);
        if (!context.Policy.Deterministic)
        {
            return fields;
        }

        return new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            ["minInclusive"] = minInclusive.ToString(CultureInfo.InvariantCulture),
            ["maxExclusive"] = maxExclusive.ToString(CultureInfo.InvariantCulture),
            ["value"] = value.ToString(CultureInfo.InvariantCulture)
        };
    }
}
