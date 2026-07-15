using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class RpcKernelCollectionReturnConversionTests
{
    [Theory]
    [InlineData("List<int>", "IList<int>")]
    [InlineData("List<int>", "IReadOnlyList<int>")]
    [InlineData("List<int>", "IReadOnlyCollection<int>")]
    [InlineData("List<int>", "IEnumerable<int>")]
    [InlineData("int[]", "IList<int>")]
    [InlineData("int[]", "IReadOnlyList<int>")]
    [InlineData("int[]", "IReadOnlyCollection<int>")]
    [InlineData("int[]", "IEnumerable<int>")]
    [InlineData("IList<int>", "IEnumerable<int>")]
    [InlineData("IReadOnlyList<int>", "IReadOnlyCollection<int>")]
    [InlineData("IReadOnlyList<int>", "IEnumerable<int>")]
    [InlineData("IReadOnlyCollection<int>", "IEnumerable<int>")]
    [InlineData("List<List<int>>", "IReadOnlyList<IReadOnlyList<int>>")]
    [InlineData("List<Dictionary<string, int>>", "IReadOnlyList<IReadOnlyDictionary<string, int>>")]
    public void Supported_list_return_conversions_remain_accepted(
        string sourceType,
        string targetType)
    {
        AssertNoErrors(CollectionReturnSource(sourceType, targetType));
    }

    [Theory]
    [InlineData("Dictionary<string, int>", "IDictionary<string, int>")]
    [InlineData("Dictionary<string, int>", "IReadOnlyDictionary<string, int>")]
    public void Supported_map_return_conversions_remain_accepted(
        string sourceType,
        string targetType)
    {
        AssertNoErrors(CollectionReturnSource(sourceType, targetType));
    }

    [Fact]
    public void List_interface_to_concrete_downcast_remains_rejected()
    {
        AssertUnsupportedConversion(CollectionReturnSource(
            "IEnumerable<int>",
            "List<int>",
            "(List<int>)values"));
    }

    [Fact]
    public void Map_interface_to_concrete_downcast_remains_rejected()
    {
        AssertUnsupportedConversion(CollectionReturnSource(
            "IDictionary<string, int>",
            "Dictionary<string, int>",
            "(Dictionary<string, int>)values"));
    }

    [Fact]
    public void User_defined_collection_conversion_remains_rejected()
    {
        AssertUnsupportedConversion("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public sealed record Carrier(int Value)
            {
                public static implicit operator List<int>(Carrier value) => [value.Value];
            }

            [ServerExtension("user-collection-conversion")]
            public sealed partial class ConversionKernel
            {
                public IReadOnlyList<int> Convert(HookContext ctx)
                {
                    var value = new Carrier(1);
                    return value;
                }
            }
            """);
    }

    [Fact]
    public void Covariant_list_conversion_with_different_element_symbols_remains_rejected()
    {
        AssertUnsupportedConversion("""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            public record BaseValue(int Value);
            public sealed record DerivedValue(int Value, int Extra) : BaseValue(Value);

            [ServerExtension("covariant-collection-conversion")]
            public sealed partial class ConversionKernel
            {
                public IEnumerable<BaseValue> Convert(
                    IReadOnlyList<DerivedValue> values,
                    HookContext ctx)
                    => values;
            }
            """);
    }

    private static string CollectionReturnSource(
        string sourceType,
        string targetType,
        string expression = "values")
        => $$"""
            using System.Collections.Generic;
            using DotBoxD.Kernels;
            using DotBoxD.Plugins;
            using DotBoxD.Abstractions;

            namespace Sample;

            [ServerExtension("collection-conversion")]
            public sealed partial class ConversionKernel
            {
                public {{targetType}} Convert({{sourceType}} values, HookContext ctx)
                    => {{expression}};
            }
            """;

    private static void AssertNoErrors(string source)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.DoesNotContain(
            diagnostics,
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static void AssertUnsupportedConversion(string source)
    {
        var diagnostics = PluginAnalyzerGeneratedPackageFactory.Diagnostics(source);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                          diagnostic.GetMessage().Contains(
                              "conversion",
                              StringComparison.OrdinalIgnoreCase));
    }
}
