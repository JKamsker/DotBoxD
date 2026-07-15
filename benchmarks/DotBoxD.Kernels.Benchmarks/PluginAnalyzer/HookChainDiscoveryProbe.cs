using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

/// <summary>
/// Measures warmed incremental edits when a source tree contains many member calls that either can or cannot
/// be hook-chain terminals. The generator driver and both pre-parsed trivia snapshots are retained.
/// </summary>
internal static class HookChainDiscoveryProbe
{
    private const int UnrelatedCallCount = 1_000;
    private const int WarmupIterations = 100;
    private const int Iterations = 1_000;
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static void Run()
    {
        var unrelated = Scenario.Create("Touch");
        var terminalNamed = Scenario.Create("Run");
        unrelated.RequireSameOutput(terminalNamed);
        RunIterations(unrelated, WarmupIterations);
        RunIterations(terminalNamed, WarmupIterations);
        unrelated.ValidateOutput();
        terminalNamed.ValidateOutput();

        Console.WriteLine($"hook-chain discovery edits = {Iterations:N0}");
        Console.WriteLine($"member calls per edited tree = {UnrelatedCallCount:N0}");
        Console.WriteLine($"generated output SHA256 = {unrelated.OutputSha256}");
        Console.WriteLine("case                         total ms    us/edit      allocated B       B/edit");
        Write("unrelated Touch calls", Measure(unrelated));
        Write("terminal-named Run control", Measure(terminalNamed));
    }

    private static Measurement Measure(Scenario scenario)
    {
        ForceGc();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        RunIterations(scenario, Iterations);
        watch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        scenario.ValidateOutput();
        return new Measurement(watch.Elapsed, allocatedBytes);
    }

    private static void RunIterations(Scenario scenario, int iterations)
    {
        for (var i = 0; i < iterations; i++)
        {
            scenario.ApplyEdit((i & 1) == 0);
        }
    }

    private static void Write(string name, Measurement measurement)
        => Console.WriteLine(
            $"{name,-28} {measurement.Elapsed.TotalMilliseconds,8:N1} " +
            $"{measurement.Elapsed.TotalMilliseconds * 1_000 / Iterations,10:N1} " +
            $"{measurement.AllocatedBytes,16:N0} " +
            $"{measurement.AllocatedBytes / (double)Iterations,12:N1}");

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string CallSource(char editMarker, string methodName)
    {
        var source = new StringBuilder();
        source.Append("// edit ").Append(editMarker).AppendLine();
        source.AppendLine("namespace HookChainDiscovery;");
        source.AppendLine("public sealed class Receiver");
        source.AppendLine("{");
        source.AppendLine("    public void Touch() { }");
        source.AppendLine("    public void Run() { }");
        source.AppendLine("}");
        source.AppendLine("public static class Workload");
        source.AppendLine("{");
        source.AppendLine("    public static void Execute(Receiver receiver)");
        source.AppendLine("    {");
        for (var i = 0; i < UnrelatedCallCount; i++)
        {
            source.Append("        receiver.").Append(methodName).AppendLine("();");
        }

        source.AppendLine("    }");
        source.AppendLine("}");
        return source.ToString();
    }

    private static string HookSource()
        => """
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Plugins.Runtime;

        namespace HookChainDiscovery;

        public sealed record DamageEvent(string TargetId);

        public static class HookConfiguration
        {
            public static void Configure(HookRegistry hooks)
                => hooks.On<DamageEvent>().Run(
                    (damage, context) => context.Messages.Send(damage.TargetId, "damage"));
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
        private readonly string _expectedOutput;
        private GeneratorDriver _driver;

        private Scenario(
            GeneratorDriver driver,
            CSharpCompilation firstCompilation,
            CSharpCompilation secondCompilation,
            string expectedOutput)
        {
            _driver = driver;
            _firstCompilation = firstCompilation;
            _secondCompilation = secondCompilation;
            _expectedOutput = expectedOutput;
        }

        public static Scenario Create(string methodName)
        {
            var hookTree = CSharpSyntaxTree.ParseText(HookSource(), ParseOptions, "Hook.cs");
            var firstCallTree = CSharpSyntaxTree.ParseText(CallSource('a', methodName), ParseOptions, "Calls.cs");
            var secondCallTree = CSharpSyntaxTree.ParseText(CallSource('b', methodName), ParseOptions, "Calls.cs");
            var firstCompilation = CSharpCompilation.Create(
                "HookChainDiscovery",
                [hookTree, firstCallTree],
                TrustedPlatformReferences()
                    .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                    .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location))
                    .Append(MetadataReference.CreateFromFile(typeof(DotBoxD.Plugins.PluginServer).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var secondCompilation = firstCompilation.ReplaceSyntaxTree(firstCallTree, secondCallTree);
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                [new DotBoxD.Plugins.Analyzer.Analysis.PluginPackageGenerator().AsSourceGenerator()],
                parseOptions: ParseOptions);
            driver = driver.RunGenerators(firstCompilation);
            var expectedOutput = Output(driver.GetRunResult());
            var scenario = new Scenario(driver, firstCompilation, secondCompilation, expectedOutput);
            scenario.ApplyEdit(useFirstCompilation: false);
            scenario.ValidateOutput();
            return scenario;
        }

        public void ApplyEdit(bool useFirstCompilation)
            => _driver = _driver.RunGenerators(useFirstCompilation ? _firstCompilation : _secondCompilation);

        public void ValidateOutput()
        {
            var result = _driver.GetRunResult();
            if (result.Diagnostics.Length != 0)
            {
                throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
            }

            var output = Output(result);
            if (!string.Equals(output, _expectedOutput, StringComparison.Ordinal) ||
                !output.Contains("HookChain_", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Hook-chain generated output changed across the trivia edit.");
            }
        }

        public void RequireSameOutput(Scenario other)
        {
            if (!string.Equals(_expectedOutput, other._expectedOutput, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Terminal-named unrelated calls changed generated hook output.");
            }
        }

        public string OutputSha256
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_expectedOutput)));

        private static string Output(GeneratorDriverRunResult result)
            => string.Join(
                "\n---\n",
                result.Results
                    .SelectMany(static generator => generator.GeneratedSources)
                    .OrderBy(static source => source.HintName, StringComparer.Ordinal)
                    .Select(static source => source.HintName + "\n" + source.SourceText));
    }
}
