using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingTypeChecks
{
    internal static void Validate(
        string bindingId,
        SandboxType type,
        List<SandboxDiagnostic> diagnostics)
    {
        if (!type.IsKnownBuiltIn())
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-BINDING-TYPE",
                $"binding '{bindingId}' exposes forbidden or unknown type '{type}'"));
        }
    }
}
