using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingDescriptorRequiredFieldValidator
{
    public static bool Validate(BindingDescriptor binding, ICollection<SandboxDiagnostic> diagnostics)
    {
        var count = diagnostics.Count;
        var parameters = binding.Parameters;

        AddDiagnosticIfNull(binding.Id, nameof(BindingDescriptor.Id), diagnostics);
        AddDiagnosticIfNull(binding.Version, nameof(BindingDescriptor.Version), diagnostics);
        AddDiagnosticIfNull(parameters, nameof(BindingDescriptor.Parameters), diagnostics);
        AddDiagnosticIfNull(binding.ReturnType, nameof(BindingDescriptor.ReturnType), diagnostics);
        AddDiagnosticIfNull(binding.CostModel, nameof(BindingDescriptor.CostModel), diagnostics);
        AddDiagnosticIfNull(binding.Invoke, nameof(BindingDescriptor.Invoke), diagnostics);
        AddDiagnosticIfNull(binding.Compiled, nameof(BindingDescriptor.Compiled), diagnostics);

        if (parameters is not null)
        {
            ValidateParameters(parameters, diagnostics);
        }

        return diagnostics.Count == count;
    }

    public static bool Validate(BindingSignature binding, ICollection<SandboxDiagnostic> diagnostics)
    {
        var count = diagnostics.Count;
        var parameters = binding.Parameters;

        AddDiagnosticIfNull(binding.Id, nameof(BindingSignature.Id), diagnostics);
        AddDiagnosticIfNull(binding.Version, nameof(BindingSignature.Version), diagnostics);
        AddDiagnosticIfNull(parameters, nameof(BindingSignature.Parameters), diagnostics);
        AddDiagnosticIfNull(binding.ReturnType, nameof(BindingSignature.ReturnType), diagnostics);
        AddDiagnosticIfNull(binding.CostModel, nameof(BindingSignature.CostModel), diagnostics);
        AddDiagnosticIfNull(binding.Compiled, nameof(BindingSignature.Compiled), diagnostics);

        if (parameters is not null)
        {
            ValidateParameters(parameters, diagnostics);
        }

        return diagnostics.Count == count;
    }

    public static bool HasRequiredFields(BindingSignature binding)
    {
        var parameters = binding.Parameters;
        return binding.Id is not null &&
            binding.Version is not null &&
            parameters is not null &&
            binding.ReturnType is not null &&
            binding.CostModel is not null &&
            binding.Compiled is not null &&
            ParametersAreNonNull(parameters);
    }

    private static void ValidateParameters(
        IReadOnlyList<SandboxType> parameters,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] is null)
            {
                diagnostics.Add(RequiredFieldDiagnostic(nameof(BindingDescriptor.Parameters)));
                return;
            }
        }
    }

    private static bool ParametersAreNonNull(IReadOnlyList<SandboxType> parameters)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] is null)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddDiagnosticIfNull(
        object? value,
        string fieldName,
        ICollection<SandboxDiagnostic> diagnostics)
    {
        if (value is null)
        {
            diagnostics.Add(RequiredFieldDiagnostic(fieldName));
        }
    }

    private static SandboxDiagnostic RequiredFieldDiagnostic(string fieldName)
        => new(
            "E-BINDING-REQUIRED",
            $"binding descriptor required field '{fieldName}' cannot be null");
}
