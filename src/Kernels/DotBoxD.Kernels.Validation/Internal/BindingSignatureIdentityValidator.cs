using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Internal;

internal static class BindingSignatureIdentityValidator
{
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
        if (BindingIdentifierValidator.TryValidate(value, out var message))
        {
            return true;
        }

        diagnostics.Add(new SandboxDiagnostic(code, $"{description} {message}", Span: span));
        return false;
    }
}
