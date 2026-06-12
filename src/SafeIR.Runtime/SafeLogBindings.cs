namespace SafeIR.Runtime;

using System.Text.RegularExpressions;
using SafeIR;

public static partial class SafeLogBindings
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
            (context, args, _) =>
            {
                Write(context, id, level, ((StringValue)args[0]).Value);
                return ValueTask.FromResult(SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static void Write(SandboxContext context, string bindingId, string level, string message)
    {
        context.ChargeLogEvent(message);
        var timestamp = context.UtcNow();
        context.Audit.Write(new SandboxAuditEvent(
            context.RunId,
            "SandboxLog",
            timestamp,
            true,
            BindingId: bindingId,
            CapabilityId: "log.write",
            Effect: SandboxEffect.Audit,
            ResourceId: $"log:{level}",
            Message: SanitizeAndRedact(message),
            Fields: BindingAuditFields.Create("log", timestamp)));
    }

    private static string SanitizeAndRedact(string message)
    {
        var chars = message.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = ' ';
            }
        }

        var sanitized = new string(chars);
        sanitized = UriCredentialPattern().Replace(sanitized, "${prefix}[redacted]@");
        sanitized = SecretPattern().Replace(sanitized, match => match.Groups["key"].Value + "[redacted]");
        return AuthSchemePattern().Replace(
            sanitized,
            match => match.Groups["scheme"].Value + " [redacted]");
    }

    [GeneratedRegex("(?i)(?<key>\\b(?:password|passwd|pwd|secret|token|access[_-]?token|refresh[_-]?token|session[_-]?token|api[_-]?key|account[_-]?key|client[_-]?secret|private[_-]?key|authorization)\\s*[:=]\\s*)(?<value>[^\\s,;]+)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(?i)\\b(?<scheme>bearer|basic)\\s+[A-Za-z0-9._~+/=-]+")]
    private static partial Regex AuthSchemePattern();

    [GeneratedRegex("(?<prefix>\\b[A-Za-z][A-Za-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@")]
    private static partial Regex UriCredentialPattern();
}
