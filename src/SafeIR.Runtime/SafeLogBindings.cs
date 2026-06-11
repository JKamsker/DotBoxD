namespace SafeIR.Runtime;

using SafeIR;

public static class SafeLogBindings
{
    public static BindingDescriptor Info { get; } = Create("log.info", "info");

    public static BindingDescriptor Warn { get; } = Create("log.warn", "warn");

    private static BindingDescriptor Create(string id, string level)
        => new(
            id,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Audit,
            "log.write",
            BindingCostModel.Fixed(2),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (context, args, _) => {
                Write(context, id, level, ((StringValue)args[0]).Value);
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static void Write(SandboxContext context, string bindingId, string level, string message)
    {
        context.ChargeLogEvent(message);
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "SandboxLog",
            DateTimeOffset.UtcNow,
            true,
            BindingId: bindingId,
            CapabilityId: "log.write",
            Effect: SandboxEffect.Audit,
            ResourceId: $"log:{level}",
            Message: Sanitize(message)));
    }

    private static string Sanitize(string message)
    {
        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++) {
            if (char.IsControl(chars[i])) {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }
}
