using System.Collections.Immutable;
using System.Reflection;
using DotBoxD.Plugins.Analyzer.Analysis.UI;
using DotBoxD.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

internal static class UiGeneratorFixture
{
    public static UiPackage Package(string source)
    {
        var result = Generate(source);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return (UiPackage)Emit(result.Output).GetType("Counter")!.GetMethod("Package")!.Invoke(null, null)!;
    }

    public static Assembly Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    public static Generation Generate(string source)
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

    internal sealed record Generation(GeneratorDriverRunResult Result, ImmutableArray<Diagnostic> Diagnostics,
        CSharpCompilation Compilation, Compilation Output, GeneratorDriver Driver);
}
