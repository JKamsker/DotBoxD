using System.Collections.Immutable;
using System.Reflection;
using DotBoxD.Plugins.Analyzer.Analysis.UI;
using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiAuthoringTests
{
    private const string Component = """
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        public static partial class Counter
        {
            [UiLocalHandler] public static int Increment(int value) => value + 1;
            [UiLocalHandler] public static string Label(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            [UiRemoteHandler(7)] public static string Search(string query) => System.IO.File.ReadAllText(query);
            public static UiPackage Package()
            {
                var b = new UiBuilder();
                var count = b.State(0);
                var label = b.Kernel(LabelUiKernel(), count);
                var increment = b.Kernel(IncrementUiKernel(), count);
                return b.Build(b.Stack(b.Text(label), b.Button("Increment", increment, count)));
            }
        }
        """;

    [Fact]
    public async Task Generated_and_handwritten_packages_have_identical_behavior_and_hash()
    {
        var generated = Package(Component);
        var builder = new UiBuilder();
        var count = builder.State(0);
        var label = builder.Kernel(new UiKernelDefinition<int, string>(generated.Kernels[0].ModuleJson, "main"), count);
        var increment = builder.Kernel(new UiKernelDefinition<int, int>(generated.Kernels[1].ModuleJson, "main"), count);
        var handwritten = builder.Build(builder.Stack(builder.Text(label), builder.Button("Increment", increment, count)));
        Assert.Equal(UiPackageJson.ComputeHash(generated, new UiPolicy()), UiPackageJson.ComputeHash(handwritten, new UiPolicy()));
        using var sandbox = UiTestFixture.Sandbox();
        await using var first = await UiTestFixture.Host(sandbox).InstallAsync(generated, new RecordingUiRenderer());
        await using var second = await UiTestFixture.Host(sandbox).InstallAsync(handwritten, new RecordingUiRenderer());
        Assert.Equal((await first.DispatchAsync(1)).State.ToArray(), (await second.DispatchAsync(1)).State.ToArray());
    }

    [Fact]
    public void Generated_output_and_package_hash_are_deterministic()
    {
        Assert.Equal(UiPackageJson.ComputeHash(Package(Component), new UiPolicy()), UiPackageJson.ComputeHash(Package(Component), new UiPolicy()));
        var result = Generate(Component);
        Assert.Contains("SearchUiEndpoint = 7", string.Join("", result.Result.GeneratedTrees), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("int Handler(int value) => System.IO.File.ReadAllText(\"x\").Length;")]
    [InlineData("int Handler(int value) => System.DateTime.Now.Second;")]
    [InlineData("object Handler(int value) => new object();")]
    [InlineData("int Handler(ref int value) => value;")]
    [InlineData("async System.Threading.Tasks.Task<int> Handler(int value) { await System.Threading.Tasks.Task.Yield(); return value; }")]
    public void Unsupported_local_code_has_actionable_compile_time_diagnostic(string handler)
    {
        var result = Generate("using DotBoxD.UI.Authoring; public static partial class Unsafe { [UiLocalHandler] public static " + handler + " }");
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Contains("[UiRemoteHandler]", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("host binding", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.NotEqual(Location.None, diagnostic.Location);
    }

    [Fact]
    public void Invariant_format_provider_must_be_the_framework_symbol()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            namespace System.Globalization
            {
                public static class CultureInfo
                {
                    public static System.IFormatProvider InvariantCulture => throw new System.Exception("plugin getter");
                }
            }
            public static partial class Provider
            {
                [UiLocalHandler] public static string Text(int value)
                    => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            """);
        Assert.Contains(result.Diagnostics, d => d.Id == "DBXU001");
    }

    [Fact]
    public void Individual_facets_can_be_disabled_and_user_members_win()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            public static partial class Manual
            {
                [UiLocalHandler(GenerateKernel = false)] public static int Disabled(int x) => x;
                [UiLocalHandler] public static int Owned(int x) => x;
                public static int Owned(int x, int y) => x + y;
                public static UiKernelDefinition<int,int> OwnedUiKernel() => new("manual", "main");
                [UiLocalHandler] public static int Generated(int x) => x + 1;
                [UiRemoteHandler(8, GenerateEndpoint = false)] public static void Remote() { }
                [UiRemoteHandler(9)] public static void OwnedRemote() { }
                public static void OwnedRemote(string value) { }
                public const int OwnedRemoteUiEndpoint = 9;
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var source = string.Join("", result.Result.GeneratedTrees);
        Assert.Contains("GeneratedUiKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OwnedUiKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DisabledUiKernel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoteUiEndpoint", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public static class Manual")]
    [InlineData("public static class Manual<T>")]
    [InlineData("file static partial class Manual")]
    public void Handwritten_facets_do_not_require_generation_container_shapes(string container)
    {
        var result = Generate("using DotBoxD.UI.Authoring; " + container + """
            {
                [UiLocalHandler] public static int Owned(int x) => x;
                public static UiKernelDefinition<int,int> OwnedUiKernel() => new("manual", "main");
                [UiRemoteHandler(9)] public static void Remote() { }
                public const int RemoteUiEndpoint = 9;
            }
            """);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Result.GeneratedTrees);
    }

    [Fact]
    public void Unrelated_syntax_change_preserves_cached_handler_output()
    {
        var initial = Generate(Component);
        var changed = initial.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal class Unrelated {}"));
        var driver = initial.Driver.RunGeneratorsAndUpdateCompilation(changed, out _, out _);
        var steps = driver.GetRunResult().Results.Single().TrackedSteps["UiLocalHandlers"];
        Assert.All(steps.SelectMany(s => s.Outputs), o => Assert.Contains(o.Reason, new[] { IncrementalStepRunReason.Unchanged, IncrementalStepRunReason.Cached }));
    }

    [Theory]
    [InlineData("UiLocalHandler")]
    [InlineData("UiRemoteHandler(7)")]
    public void File_local_handler_containers_have_actionable_diagnostics(string attribute)
    {
        var result = Generate("using DotBoxD.UI.Authoring; file static partial class Handlers { [" +
            attribute + "] public static int Increment(int value) => value + 1; }");
        var diagnostic = Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Contains("file-local", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(result.Result.GeneratedTrees);
    }

    private static UiPackage Package(string source)
    {
        var result = Generate(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        using var stream = new MemoryStream();
        var emitted = result.Output.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        return (UiPackage)assembly.GetType("Counter")!.GetMethod("Package")!.Invoke(null, null)!;
    }

    private static Generation Generate(string source)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Append(typeof(UiPackage).Assembly.Location).Distinct(StringComparer.Ordinal)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("UiGenerated" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new UiHandlerGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new(driver.GetRunResult(), diagnostics, compilation, output, driver);
    }

    private sealed record Generation(GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics,
        CSharpCompilation Compilation, Compilation Output, GeneratorDriver Driver);
}
