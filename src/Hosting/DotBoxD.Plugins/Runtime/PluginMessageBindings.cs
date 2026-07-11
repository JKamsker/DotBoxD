using System.Globalization;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using CompiledRuntime = DotBoxD.Kernels.Runtime.CompiledRuntime;

namespace DotBoxD.Plugins.Runtime;

public static class PluginMessageBindings
{
    public const string SendBindingId = "host.message.send";
    public const string CapabilityId = "host.message.write";

    public static SandboxHostBuilder AddPluginMessageBindings(
        this SandboxHostBuilder builder,
        IPluginMessageSink sink)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddBinding(CreateSend(sink));
        return builder;
    }

    public static BindingDescriptor CreateSend(IPluginMessageSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var invoker = new PluginMessageSendInvoker(sink);
        return new BindingDescriptor(
            SendBindingId,
            SemVersion.One,
            [SandboxType.String, SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            CapabilityId,
            new BindingCostModel(5, MaxCallsPerRun: 100),
            AuditLevel.PerResource,
            BindingSafety.SideEffectingExternal,
            invoker.Invoke,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
            PluginMessageGrantPolicy.Validate)
        {
            IsAsync = true,
            AuditKind = BindingAuditKinds.PluginMessage
        };
    }

    private static string Sanitize(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return SanitizeWithControlCharacters(value);
            }
        }

        return value;
    }

    private static string SanitizeWithControlCharacters(string value)
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

    private static string SanitizeResourceTargetId(string targetId)
    {
        var sanitized = AuditTextSanitizer.SanitizeAndRedact(targetId);
        return string.Equals(sanitized, targetId, StringComparison.Ordinal)
            ? targetId
            : "[redacted]";
    }

    private static void WriteAudit(SandboxContext context, string targetId, string message)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var fields = BindingAuditFields.CreateMutable(
            "plugin-message",
            timestamp,
            context.ModuleHash,
            context.PolicyHash,
            context.Policy.Deterministic,
            extraCapacity: 1);
        fields["messageLength"] = message.Length.ToString(CultureInfo.InvariantCulture);
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            BindingAuditKinds.PluginMessage,
            timestamp,
            true,
            BindingId: SendBindingId,
            CapabilityId: CapabilityId,
            Effect: SandboxEffect.HostStateWrite,
            ResourceId: $"target:{SanitizeResourceTargetId(targetId)}",
            Message: AuditTextSanitizer.SanitizeAndRedact(message),
            Fields: fields));
    }

    private sealed class PluginMessageSendInvoker(IPluginMessageSink sink) : ITwoArgumentBindingInvoker
    {
        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
            => Invoke(context, args[0], args[1], cancellationToken);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            CancellationToken cancellationToken)
        {
            var targetId = ((StringValue)arg0).Value;
            if (!SandboxLiteralConstraints.IsOpaqueId(targetId))
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.InvalidInput,
                    "host.message.send denied: target ID is invalid"));
            }

            var options = PluginMessageGrantPolicy.ReadOptions(context.GetCapability(CapabilityId));
            if (!options.AllowsTarget(targetId))
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.PermissionDenied,
                    "host.message.send denied: target is not in the granted recipient set"));
            }

            var message = Sanitize(((StringValue)arg1).Value);
            if (options.MaxMessageLength is { } maxMessageLength && message.Length > maxMessageLength)
            {
                throw new SandboxRuntimeException(new SandboxError(
                    SandboxErrorCode.QuotaExceeded,
                    "host.message.send denied: message exceeds the granted length limit"));
            }

            cancellationToken.ThrowIfCancellationRequested();
            context.CancellationToken.ThrowIfCancellationRequested();
            var send = sink.SendAsync(targetId, message, cancellationToken);
            if (!send.IsCompletedSuccessfully)
            {
                return AwaitSendAsync(send, context, targetId, message);
            }

            send.GetAwaiter().GetResult();
            WriteAudit(context, targetId, message);
            return ValueTask.FromResult(SandboxValue.Unit);
        }

        private static async ValueTask<SandboxValue> AwaitSendAsync(
            ValueTask send,
            SandboxContext context,
            string targetId,
            string message)
        {
            await send.ConfigureAwait(false);
            WriteAudit(context, targetId, message);
            return SandboxValue.Unit;
        }
    }
}
