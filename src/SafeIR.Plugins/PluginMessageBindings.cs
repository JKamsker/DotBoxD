namespace SafeIR.Plugins;

using SafeIR;
using SafeIR.Hosting;
using SafeIR.Runtime;

public static class PluginMessageBindings
{
    public const string SendBindingId = "game.message.send";
    public const string CapabilityId = "game.message.write";

    public static SandboxHostBuilder AddPluginMessageBindings(
        this SandboxHostBuilder builder,
        IPluginMessageSink sink)
    {
        builder.AddBinding(CreateSend(sink));
        return builder;
    }

    public static BindingDescriptor CreateSend(IPluginMessageSink sink)
        => new(
            SendBindingId,
            SemVersion.One,
            [SandboxType.String, SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.GameStateWrite | SandboxEffect.Audit,
            CapabilityId,
            new BindingCostModel(5, MaxCallsPerRun: 100),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            async (context, args, cancellationToken) =>
            {
                var targetId = ((StringValue)args[0]).Value;
                var message = Sanitize(((StringValue)args[1]).Value);
                await sink.SendAsync(targetId, message, cancellationToken).ConfigureAwait(false);
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    "PluginMessage",
                    DateTimeOffset.UtcNow,
                    true,
                    BindingId: SendBindingId,
                    CapabilityId: CapabilityId,
                    Effect: SandboxEffect.GameStateWrite,
                    ResourceId: $"player:{targetId}",
                    Message: message));
                return SandboxValue.Unit;
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            ValidateGrant);

    private static void ValidateGrant(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }

    private static string Sanitize(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        return new string(chars);
    }
}
