using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Validation;

using DotBoxD.Kernels;
using DotBoxD.Kernels.Validation.Internal;

internal static class StructuralValidator
{
    public static void Validate(
        SandboxModule module,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        CheckIdentifier(module.Id, "module id", diagnostics);
        CheckRequiredVersion(module.Version, "module version", diagnostics);
        if (module.TargetSandboxVersion is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-VERSION", "target sandbox version must not be null"));
        }
        else if (!SandboxLanguage.Supports(module.TargetSandboxVersion))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-IR-VERSION",
                $"target sandbox version '{module.TargetSandboxVersion}' is not supported by runtime '{SandboxLanguage.CurrentVersionText}'"));
        }

        var hasNullCapabilityRequests = CheckNullEntries(module.CapabilityRequests, "capabilityRequests", null, diagnostics);
        foreach (var request in module.CapabilityRequests)
        {
            if (request is null)
            {
                continue;
            }

            CheckCapabilityRequest(request, diagnostics);
            CheckOptionalText(request.Reason, "capability reason", diagnostics);
        }

        if (!hasNullCapabilityRequests)
        {
            StructuralDuplicateValidator.CheckCapabilityRequests(module.CapabilityRequests, diagnostics);
        }

        foreach (var item in module.Metadata)
        {
            CheckIdentifier(item.Key, "metadata key", diagnostics);
            CheckText(item.Value, $"metadata value for key '{item.Key}'", diagnostics);
        }

        FunctionCollectionValidator.Validate(module.Functions, diagnostics);

        foreach (var function in module.Functions)
        {
            if (function is not null)
            {
                ValidateFunction(function, diagnostics, declaredOpaqueIdTypes);
            }
        }
    }

    private static void CheckRequiredVersion(SemVersion? version, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (version is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-VERSION", $"{description} must not be null"));
        }
    }

    private static void ValidateFunction(
        SandboxFunction function,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        CheckIdentifier(function.Id, "function id", diagnostics);
        CheckType(function.ReturnType, $"function '{function.Id}' return type", diagnostics, declaredOpaqueIdTypes);
        CheckDeclaredEffects(function, diagnostics);
        var hasNullParameters = CheckNullEntries(function.Parameters, "parameters", function.Id, diagnostics);
        CheckNullEntries(function.Body, "body", function.Id, diagnostics);

        if (!hasNullParameters)
        {
            StructuralDuplicateValidator.CheckParameters(function, diagnostics);
            foreach (var parameter in function.Parameters)
            {
                CheckIdentifier(parameter.Name, "parameter name", diagnostics);
                CheckType(
                    parameter.Type,
                    $"function '{function.Id}' parameter '{parameter.Name}' type",
                    diagnostics,
                    declaredOpaqueIdTypes);
            }
        }

        foreach (var statement in function.Body)
        {
            if (statement is null)
            {
                continue;
            }

            IrNullValidator.Scan(statement, diagnostics);
            DangerousReferenceDetector.Scan(statement, diagnostics);
        }
    }

    private static bool CheckNullEntries<T>(
        IReadOnlyList<T> values,
        string collectionName,
        string? functionId,
        List<SandboxDiagnostic> diagnostics)
    {
        var hasNull = false;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is not null)
            {
                continue;
            }

            hasNull = true;
            var message = functionId is null
                ? $"{collectionName} entry at index {i} must not be null"
                : $"function '{functionId}' {collectionName} entry at index {i} must not be null";
            diagnostics.Add(new SandboxDiagnostic(
                "E-STRUCT-NULL",
                message));
        }

        return hasNull;
    }

    private static void CheckCapabilityRequest(CapabilityRequest request, List<SandboxDiagnostic> diagnostics)
    {
        CheckIdentifier(request.Id, "capability id", diagnostics);
        if (request.Id is not null &&
            (CapabilityPattern.IsWildcard(request.Id) || request.Id.EndsWith(".", StringComparison.Ordinal)))
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-IR-CAPABILITY",
                $"capability request '{request.Id}' must be concrete"));
        }
    }

    private static void CheckDeclaredEffects(SandboxFunction function, List<SandboxDiagnostic> diagnostics)
    {
        if (function.DeclaredEffects is { } effects && !effects.ContainsOnlyKnownBits())
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-EFFECT",
                $"function '{function.Id}' has declared unknown effects"));
        }
    }

    private static void CheckType(
        SandboxType? type,
        string description,
        List<SandboxDiagnostic> diagnostics,
        IReadOnlySet<string> declaredOpaqueIdTypes)
    {
        if (type is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-NULL", $"{description} must not be null"));
            return;
        }

        if (type.Name == "Map" && type.Arguments.Count == 2 && !type.Arguments[0].IsValidMapKey(declaredOpaqueIdTypes))
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-MAP-KEY", $"map key type '{type.Arguments[0]}' is not supported"));
        }

        if (!type.IsKnown(declaredOpaqueIdTypes))
        {
            diagnostics.Add(new SandboxDiagnostic("E-TYPE-UNKNOWN", $"unknown or forbidden type '{type}'"));
        }
    }

    private static void CheckIdentifier(string value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-ID", $"{description} must be non-empty and must not contain control characters"));
            return;
        }

        if (DangerousReferenceDetector.IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }

    private static void CheckOptionalText(string? value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (value is not null)
        {
            CheckText(value, description, diagnostics);
        }
    }

    private static void CheckText(string? value, string description, List<SandboxDiagnostic> diagnostics)
    {
        if (value is null)
        {
            diagnostics.Add(new SandboxDiagnostic("E-STRUCT-NULL", $"{description} must not be null"));
            return;
        }

        if (DangerousReferenceDetector.IsDangerousReference(value))
        {
            diagnostics.Add(new SandboxDiagnostic("E-IR-CLR-REF", $"{description} '{value}' looks like a forbidden CLR reference"));
        }
    }
}
