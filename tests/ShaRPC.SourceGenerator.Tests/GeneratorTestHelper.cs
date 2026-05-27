using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ShaRPC.Core.Attributes;

namespace ShaRPC.SourceGenerator.Tests;

/// <summary>
/// Helpers for constructing in-memory compilations and driving the ShaRPC generator
/// with tracking enabled so incrementality assertions can inspect tracked step outputs.
/// </summary>
internal static class GeneratorTestHelper
{
    /// <summary>
    /// Builds a compilation that contains the supplied user source plus references to
    /// .NET 9 BCL and the ShaRPC.Core marker attribute assembly.
    /// </summary>
    public static CSharpCompilation CreateCompilation(params string[] sources)
    {
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray();

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net80.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ShaRpcServiceAttribute).Assembly.Location),
        };

        return CSharpCompilation.Create(
            assemblyName: "compilation",
            syntaxTrees: trees,
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    /// <summary>
    /// Returns a fresh driver with incremental step tracking enabled.
    /// </summary>
    public static GeneratorDriver CreateDriver()
    {
        var generator = new ShaRpcGenerator().AsSourceGenerator();
        return CSharpGeneratorDriver.Create(
            generators: new[] { generator },
            driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: true));
    }

    /// <summary>
    /// Convenience: build, run, return (driver, compilation).
    /// </summary>
    public static (GeneratorDriver Driver, Compilation Compilation) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CreateDriver().RunGenerators(compilation);
        return (driver, compilation);
    }
}
