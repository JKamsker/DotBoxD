using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Validation.Bindings;

internal static class CatalogBindingSignatureValidator
{
    public static void ValidateReferenced(
        IReadOnlyDictionary<string, IReadOnlySet<string>> bindingReferences,
        IBindingCatalog bindings,
        List<SandboxDiagnostic> diagnostics)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var references in bindingReferences.Values)
        {
            foreach (var bindingId in references)
            {
                if (visited.Add(bindingId) && bindings.TryGet(bindingId, out var binding))
                {
                    ValidateBinding(binding, diagnostics);
                }
            }
        }
    }

    private static void ValidateBinding(BindingSignature binding, List<SandboxDiagnostic> diagnostics)
    {
        if (binding.Safety == BindingSafety.DangerousRequiresReview)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-DANGER",
                $"binding '{binding.Id}' is dangerous and cannot be enabled by default"));
        }
    }
}
