using DotBoxD.Kernels.Model;

namespace DotBoxD.Plugins.Runtime.Validation;

internal static class PluginManifestPredicateValidator
{
    public static void ValidateIndexedPredicates(
        HookSubscriptionManifest subscription,
        List<SandboxDiagnostic> diagnostics)
    {
        var indexedPredicates = subscription.IndexedPredicates;
        if (!PluginManifestElementValidator.ValidateNoNullElements(
            indexedPredicates,
            "indexedPredicates",
            diagnostics))
        {
            return;
        }

        foreach (var predicate in indexedPredicates)
        {
            PluginManifestTextValidator.ValidateText(predicate.Path, "indexed predicate path", diagnostics);
            if (!Enum.IsDefined(predicate.Operator))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK046",
                    $"Indexed predicate operator '{predicate.Operator}' is not supported."));
            }

            if (predicate.ValueType is not ("bool" or "int" or "long" or "double" or "string"))
            {
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK047",
                    $"Indexed predicate value type '{predicate.ValueType}' is not supported."));
            }
            else if (!ValueMatchesType(predicate.Value, predicate.ValueType))
            {
                // JSON import already parses by valueType; this closes in-memory package construction.
                diagnostics.Add(new SandboxDiagnostic(
                    "DBXK049",
                    $"Indexed predicate value '{predicate.Value ?? "null"}' does not match its declared value type '{predicate.ValueType}'."));
            }
        }

        if (subscription.IndexCoversPredicate && indexedPredicates.Count == 0)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "DBXK048",
                "A hook subscription cannot claim full index coverage with no indexed predicates."));
        }
    }

    private static bool ValueMatchesType(object? value, string valueType)
        => valueType switch
        {
            "bool" => value is bool,
            "int" => value is int,
            "long" => value is long,
            "double" => value is double,
            "string" => value is string,
            _ => false
        };
}
