using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

internal static class BindingSignatureIdentityValidator
{
    private static readonly string[] ForbiddenReferenceFragments = [
        "System.", "Microsoft.", "Assembly.", "Type.", "Reflection.", "Process.",
        "Environment.", "Thread.", "Task.", "DllImport", "IServiceProvider"
    ];

    public static bool ValidateResolved(
        string lookupId,
        BindingSignature? binding,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan span)
    {
        if (binding is null)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-ID",
                $"binding catalog returned a null signature for lookup '{lookupId}'",
                Span: span));
            return false;
        }

        var valid = ValidateIdentifier(binding.Id, "binding id", "E-BINDING-ID", diagnostics, span);
        if (binding.RequiredCapability is not null)
        {
            valid &= ValidateIdentifier(
                binding.RequiredCapability,
                "required capability",
                "E-BINDING-CAP",
                diagnostics,
                span);
        }

        if (!string.Equals(lookupId, binding.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-ID",
                $"binding catalog returned binding id '{binding.Id}' for lookup '{lookupId}'",
                Span: span));
            valid = false;
        }

        return valid;
    }

    private static bool ValidateIdentifier(
        string? value,
        string description,
        string code,
        List<SandboxDiagnostic> diagnostics,
        SourceSpan span)
    {
        if (string.IsNullOrWhiteSpace(value) || ContainsControlCharacter(value))
        {
            diagnostics.Add(new SandboxDiagnostic(
                code,
                $"{description} must be non-empty and must not contain control characters",
                Span: span));
            return false;
        }

        if (!ContainsForbiddenReferenceFragment(value))
        {
            return true;
        }

        diagnostics.Add(new SandboxDiagnostic(code, $"{description} '{value}' looks like a forbidden CLR reference", Span: span));
        return false;
    }

    private static bool ContainsControlCharacter(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsControl(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForbiddenReferenceFragment(string value)
    {
        for (var i = 0; i < ForbiddenReferenceFragments.Length; i++)
        {
            if (value.Contains(ForbiddenReferenceFragments[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
