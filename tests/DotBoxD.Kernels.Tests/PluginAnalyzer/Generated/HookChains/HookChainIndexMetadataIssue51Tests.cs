using System.Reflection;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class HookChainIndexMetadataIssue51Tests
{
    [Fact]
    public void Nested_event_property_path_is_fully_covered()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.NestedIndexEvent>()
                .Where(e => e.Damage.Amount >= 5 && e.Damage.Kind == "fire")
                .Select(e => e.TargetId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.True(manifest.IndexCoversPredicate);
        Assert.Collection(
            manifest.IndexedPredicates,
            p =>
            {
                Assert.Equal("Damage.Amount", p.Path);
                Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, p.Operator);
                Assert.Equal(5, Assert.IsType<int>(p.Value));
                Assert.Equal("int", p.ValueType);
            },
            p =>
            {
                Assert.Equal("Damage.Kind", p.Path);
                Assert.Equal(IndexPredicateOperator.Equals, p.Operator);
                Assert.Equal("fire", p.Value);
                Assert.Equal("string", p.ValueType);
            });
    }

    [Fact]
    public void A_nested_non_property_leaf_yields_no_index_metadata()
    {
        var manifest = GeneratedSubscription(
            """
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Generated.NestedIndexEvent>()
                .Where(e => e.Damage.Kind.Length > 3)
                .Select(e => e.TargetId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.False(manifest.IndexCoversPredicate);
        Assert.Empty(manifest.IndexedPredicates);
    }

    [Fact]
    public void Captured_effectively_final_local_constant_is_fully_covered()
    {
        var manifest = GeneratedSubscriptionFromBody(
            """
            var minimumDistance = 5;
            subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                .Where(e => e.Distance >= minimumDistance)
                .Select(e => e.MonsterId)
                .Run((id, ctx) => ctx.Messages.Send(id, "calm"));
            """);

        Assert.True(manifest.IndexCoversPredicate);
        var predicate = Assert.Single(manifest.IndexedPredicates);
        Assert.Equal("Distance", predicate.Path);
        Assert.Equal(IndexPredicateOperator.GreaterThanOrEqual, predicate.Operator);
        Assert.Equal(5, Assert.IsType<int>(predicate.Value));
        Assert.Equal("int", predicate.ValueType);
    }

    private static HookSubscriptionManifest GeneratedSubscription(string chain)
        => GeneratedPackageFromExpression(chain).Manifest.Subscriptions.Single();

    private static HookSubscriptionManifest GeneratedSubscriptionFromBody(string body)
        => GeneratedPackageFromBody(body).Manifest.Subscriptions.Single();

    private static PluginPackage GeneratedPackageFromExpression(string chain)
    {
        var source = $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                    => {{chain}}
            }
            """;

        return CompilePackage(source);
    }

    private static PluginPackage GeneratedPackageFromBody(string body)
    {
        var source = $$"""
            using DotBoxD.Plugins.Runtime;

            namespace ChainSample;

            public static class Usage
            {
                public static void Configure(SubscriptionRegistry subscriptions)
                {
                    {{body}}
                }
            }
            """;

        return CompilePackage(source);
    }

    private static PluginPackage CompilePackage(string source)
    {
        var assembly = Compile(source);
        var packageType = assembly.GetTypes().Single(type =>
            type.Name.StartsWith("HookChain_", StringComparison.Ordinal) &&
            type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is not null);
        var create = packageType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!;
        return (PluginPackage)create.Invoke(null, null)!;
    }

    private static Assembly Compile(string source)
    {
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        var compilation = CSharpCompilation.Create(
            "DotBoxDIndexMetadataIssue51Test",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(Runtime.ChainAggroEvent).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new PluginPackageGenerator().AsSourceGenerator()],
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics.Select(d => d.ToString())));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}

public sealed record NestedDamage(int Amount, string Kind);

public sealed record NestedIndexEvent(NestedDamage Damage, string TargetId);
