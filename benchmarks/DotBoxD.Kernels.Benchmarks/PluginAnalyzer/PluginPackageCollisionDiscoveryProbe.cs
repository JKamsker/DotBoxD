using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

/// <summary>
/// Measures a warmed incremental generator edit when a project contains many source types that cannot collide
/// with a generated plugin package. The returned driver is retained between two pre-parsed trivia snapshots.
/// </summary>
internal static class PluginPackageCollisionDiscoveryProbe
{
    private const int UnrelatedTypeCount = 1_000;
    private const int WarmupIterations = 100;
    private const int Iterations = 1_000;
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static void Run()
    {
        var scenario = Scenario.Create();
        RunIterations(scenario, WarmupIterations);
        scenario.ValidateCollision();
        ForceGc();

        var measurement = Measure(scenario, Iterations);
        scenario.ValidateCollision();
        Console.WriteLine($"plugin package collision edits = {Iterations:N0}");
        Console.WriteLine($"unrelated source types = {UnrelatedTypeCount:N0}");
        Console.WriteLine(
            $"alternating trivia: {measurement.Elapsed.TotalMilliseconds:N1} ms, " +
            $"{measurement.Elapsed.TotalMilliseconds * 1_000 / Iterations:N1} us/edit, " +
            $"{measurement.AllocatedBytes:N0} B, " +
            $"{measurement.AllocatedBytes / (double)Iterations:N1} B/edit");
    }

    private static void RunIterations(Scenario scenario, int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            scenario.ApplyEdit((i & 1) == 0);
        }
    }

    private static Measurement Measure(Scenario scenario, int iterations)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        RunIterations(scenario, iterations);
        watch.Stop();
        return new Measurement(
            watch.Elapsed,
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string SourceTypes(char editMarker)
    {
        var source = new StringBuilder();
        source.Append("// edit ").Append(editMarker).AppendLine();
        source.AppendLine("namespace CollisionDiscovery;");
        for (var i = 0; i < UnrelatedTypeCount; i++)
        {
            source.Append("public sealed class UnrelatedType").Append(i).AppendLine(" { }");
        }

        source.AppendLine("internal sealed class CollisionDiscoveryPluginPackage { }");
        return source.ToString();
    }

    private static string KernelSource()
        => """
        using DotBoxD.Plugins;
        using DotBoxD.Abstractions;

        namespace CollisionDiscovery;

        [Plugin("collision-discovery")]
        public sealed partial class CollisionDiscoveryKernel : IEventKernel<int>
        {
            public bool ShouldHandle(int e, HookContext context) => true;
            public void Handle(int e, HookContext context) => context.Messages.Send("target", "message");
        }
        """;

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
        => (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Select(reference => MetadataReference.CreateFromFile(reference));

    private readonly record struct Measurement(TimeSpan Elapsed, long AllocatedBytes);

    private sealed class Scenario
    {
        private readonly CSharpCompilation _firstCompilation;
        private readonly CSharpCompilation _secondCompilation;
        private GeneratorDriver _driver;

        private Scenario(
            GeneratorDriver driver,
            CSharpCompilation firstCompilation,
            CSharpCompilation secondCompilation)
        {
            _driver = driver;
            _firstCompilation = firstCompilation;
            _secondCompilation = secondCompilation;
        }

        public static Scenario Create()
        {
            var kernelTree = CSharpSyntaxTree.ParseText(KernelSource(), ParseOptions, "Kernel.cs");
            var firstSourceTypesTree = CSharpSyntaxTree.ParseText(SourceTypes('a'), ParseOptions, "SourceTypes.cs");
            var secondSourceTypesTree = CSharpSyntaxTree.ParseText(SourceTypes('b'), ParseOptions, "SourceTypes.cs");
            var firstCompilation = CSharpCompilation.Create(
                "PluginPackageCollisionDiscovery",
                [kernelTree, firstSourceTypesTree],
                TrustedPlatformReferences()
                    .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                    .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstSourceTypesTree, secondSourceTypesTree);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator().AsSourceGenerator()],
                parseOptions: ParseOptions);
            driver = driver.RunGenerators(firstCompilation);
            var scenario = new Scenario(driver, firstCompilation, secondCompilation);
            scenario.ValidateCollision();
            return scenario;
        }

        public void ApplyEdit(bool useFirstCompilation)
            => _driver = _driver.RunGenerators(useFirstCompilation ? _firstCompilation : _secondCompilation);

        public void ValidateCollision()
        {
            var result = _driver.GetRunResult();
            static bool IsCollision(Diagnostic diagnostic)
                => diagnostic.Id == "DBXK100" &&
                   diagnostic.GetMessage().Contains("collides with an existing type", StringComparison.Ordinal);

            if (result.Diagnostics.Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error && !IsCollision(diagnostic)))
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
            }

            if (!result.Diagnostics.Any(IsCollision))
            {
                throw new InvalidOperationException("Expected a generated package source-collision diagnostic.");
            }

            if (result.GeneratedTrees.Length != 0)
            {
                throw new InvalidOperationException($"Expected the collision to block generation, got {result.GeneratedTrees.Length} trees.");
            }
        }
    }
}
