using System.Reflection;
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Analyzer.Analysis;
using DotBoxD.Plugins.Runtime;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class RemoteHostCallbackChainTests
{
    private const string Source = """
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using DotBoxD.Plugins.Runtime;

        namespace ChainSample;

        public static class RemoteUsage
        {
            public static readonly List<string> Seen = new();

            public static void Configure(RemoteSubscriptionRegistry subscriptions)
                => subscriptions.On<global::DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime.ChainAggroEvent>()
                    .Where(e => e.Distance <= 5)
                    .RunHost((e, ctx) =>
                    {
                        Seen.Add(e.MonsterId);
                        return ValueTask.CompletedTask;
                    });
        }
        """;

    [Fact]
    public async Task RunHost_installs_an_indexed_filter_package_and_keeps_the_host_callback()
    {
        var assembly = Compile(Source, enableInterceptors: true);
        var installed = new List<PluginPackage>();
        var callbacks = new List<RemoteHostCallbackRegistration>();
        var registry = new RemoteSubscriptionRegistry(
            package =>
            {
                installed.Add(package);
                return ValueTask.FromResult(package.Manifest.PluginId);
            },
            registration =>
            {
                callbacks.Add(registration);
                return ValueTask.FromResult(registration.Package.Manifest.PluginId);
            });

        Configure(assembly, registry);

        Assert.Empty(installed);
        var callback = Assert.Single(callbacks);
        Assert.Equal(typeof(ChainAggroEvent), callback.EventType);
        var subscription = Assert.Single(callback.Package.Manifest.Subscriptions);
        Assert.True(subscription.IndexCoversPredicate);
        var predicate = Assert.Single(subscription.IndexedPredicates);
        Assert.Equal("Distance", predicate.Path);
        Assert.Equal(IndexPredicateOperator.LessThanOrEqual, predicate.Operator);
        Assert.Equal(5, Assert.IsType<int>(predicate.Value));
        Assert.DoesNotContain(PluginMessageBindings.CapabilityId, callback.Package.Manifest.RequiredCapabilities);

        var handler = Assert.IsType<Func<ChainAggroEvent, HookContext, ValueTask>>(callback.Handler);
        await handler(
            new ChainAggroEvent("monster-1", 3),
            new HookContext(new InMemoryPluginMessageSink(), CancellationToken.None));

        Assert.Equal(["monster-1"], Seen(assembly));
    }

    private static void Configure(Assembly assembly, RemoteSubscriptionRegistry registry)
        => assembly.GetType("ChainSample.RemoteUsage")!
            .GetMethod("Configure", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [registry]);

    private static IReadOnlyList<string> Seen(Assembly assembly)
        => (IReadOnlyList<string>)assembly.GetType("ChainSample.RemoteUsage")!
            .GetField("Seen", BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;

    private static Assembly Compile(string source, bool enableInterceptors)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        if (enableInterceptors)
        {
            parseOptions = parseOptions.WithFeatures(
                [new KeyValuePair<string, string>("InterceptorsNamespaces", "DotBoxD.Plugins.Generated")]);
        }

        var compilation = CSharpCompilation.Create(
            "DotBoxDRemoteHostCallbackChainTest",
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(PluginPackage).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(ChainAggroEvent).Assembly.Location)),
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
