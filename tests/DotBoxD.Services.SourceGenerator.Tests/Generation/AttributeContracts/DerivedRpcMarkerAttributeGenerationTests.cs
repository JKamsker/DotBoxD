using Microsoft.CodeAnalysis;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.CodegenRegressionTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class DerivedRpcMarkerAttributeGenerationTests
{
    [Fact]
    public void DerivedRpcMarkerAttributesParticipateInServiceAndMethodGeneration()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.DerivedRpcMarkers
            {
                public sealed class CustomRpcServiceAttribute : RpcServiceAttribute
                {
                }

                public sealed class CustomRpcMethodAttribute : RpcMethodAttribute
                {
                }

                [RpcService]
                public interface IExactMarker
                {
                    [RpcMethod(Name = "ExactControlWire")]
                    Task<int> ControlAsync();

                    [CustomRpcMethod(Name = "ExactDerivedMethodWire")]
                    Task<int> RenamedAsync();
                }

                [CustomRpcService]
                public interface IDerivedMarker
                {
                    [CustomRpcMethod(Name = "DerivedServiceWire")]
                    Task<int> PingAsync();
                }
            }
            """;

        var (final, runResult) = Run(source);

        Assert.Empty(runResult.Diagnostics.Where(IsDbxsError));
        AssertCompiles(final);

        var sources = runResult.Results.Single().GeneratedSources.ToDictionary(
            source => source.HintName,
            source => source.SourceText.ToString(),
            StringComparer.Ordinal);
        var missing = new List<string>();

        var exactDispatcher = RequireSource(
            sources,
            GeneratorTestHelper.HintName(
                "Regress.DerivedRpcMarkers",
                "IExactMarker",
                GeneratorTestHelper.GeneratedKind.Dispatcher),
            "exact-marker control dispatcher",
            missing);
        var factory = RequireSource(sources, "DotBoxDGenerated.g.cs", "generated service registry metadata", missing);

        RequireContains(exactDispatcher, "case \"ExactControlWire\":", "exact RpcMethod wire name", missing);
        RequireContains(
            exactDispatcher,
            "case \"ExactDerivedMethodWire\":",
            "derived RpcMethod wire name on exact service",
            missing);
        RequireContains(factory, "\"ExactDerivedMethodWire\"", "derived RpcMethod registry metadata", missing);

        RequireSource(
            sources,
            GeneratorTestHelper.HintName(
                "Regress.DerivedRpcMarkers",
                "IDerivedMarker",
                GeneratorTestHelper.GeneratedKind.Proxy),
            "derived RpcService proxy",
            missing);
        var derivedDispatcher = RequireSource(
            sources,
            GeneratorTestHelper.HintName(
                "Regress.DerivedRpcMarkers",
                "IDerivedMarker",
                GeneratorTestHelper.GeneratedKind.Dispatcher),
            "derived RpcService dispatcher",
            missing);
        RequireSource(
            sources,
            GeneratorTestHelper.HintName(
                "Regress.DerivedRpcMarkers",
                "IDerivedMarker",
                GeneratorTestHelper.GeneratedKind.Async),
            "derived RpcService async sibling",
            missing);

        RequireContains(
            factory,
            "typeof(global::Regress.DerivedRpcMarkers.IDerivedMarker)",
            "derived RpcService registry service type",
            missing);
        RequireContains(factory, "\"DerivedServiceWire\"", "derived RpcService method registry metadata", missing);
        RequireContains(
            derivedDispatcher,
            "case \"DerivedServiceWire\":",
            "derived RpcService dispatcher method wire name",
            missing);

        Assert.True(missing.Count == 0, "Missing generated derived marker support: " + string.Join(", ", missing));
    }

    private static string RequireSource(
        IReadOnlyDictionary<string, string> sources,
        string hintName,
        string description,
        List<string> missing)
    {
        if (sources.TryGetValue(hintName, out var source))
        {
            return source;
        }

        missing.Add(description + " (" + hintName + ")");
        return string.Empty;
    }

    private static void RequireContains(string source, string expected, string description, List<string> missing)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            missing.Add(description + " (" + expected + ")");
        }
    }

    private static bool IsDbxsError(Diagnostic diagnostic)
        => diagnostic.Severity == DiagnosticSeverity.Error &&
           diagnostic.Id.StartsWith("DBXS", StringComparison.Ordinal);
}
