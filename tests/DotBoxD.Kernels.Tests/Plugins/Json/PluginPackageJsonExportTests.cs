using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Json;

namespace DotBoxD.Kernels.Tests.Plugins.Json;

public sealed class PluginPackageJsonExportTests
{
    public static TheoryData<object?, string, string> InvalidIndexedPredicateValues { get; } = new()
    {
        { 5, "short", "DBXK047" },
        { 2147483648L, "int", "DBXK049" },
        { "true", "bool", "DBXK049" },
        { null, "int", "DBXK049" },
    };

    [Fact]
    public void Export_rejects_undefined_indexed_predicate_operator()
    {
        var invalid = WithIndexedPredicate(new IndexedPredicate("Damage", (IndexPredicateOperator)999, 5, "int"));

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == "DBXK046");
    }

    [Theory]
    [MemberData(nameof(InvalidIndexedPredicateValues))]
    public void Export_rejects_indexed_predicate_values_that_import_would_reject(
        object? value,
        string valueType,
        string expectedCode)
    {
        var invalid = WithIndexedPredicate(new IndexedPredicate(
            "Damage",
            IndexPredicateOperator.Equals,
            value,
            valueType));

        var ex = Assert.Throws<SandboxValidationException>(() => PluginPackageJsonSerializer.Export(invalid));

        Assert.Contains(ex.Diagnostics, d => d.Code == expectedCode);
    }

    private static PluginPackage WithIndexedPredicate(IndexedPredicate predicate)
    {
        var package = FireDamagePluginPackage.Create();
        var subscription = package.Manifest.Subscriptions[0];

        return package with
        {
            Manifest = package.Manifest with
            {
                Subscriptions =
                [
                    subscription with
                    {
                        IndexedPredicates = [predicate],
                    }
                ],
            },
        };
    }
}
