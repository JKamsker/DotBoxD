namespace DotBoxD.Kernels.Benchmarks.PluginAnalyzer;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

internal static class GenericConstructionReachabilityProbeSources
{
    private static readonly CSharpParseOptions ParseOptions =
        CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

    public static string GenericChain(int forwardingCount, bool descending)
    {
        var source = StartSource();
        source.AppendLine("public static class GenericFactory");
        source.AppendLine("{");
        for (var offset = 0; offset <= forwardingCount; offset++)
        {
            var index = descending ? forwardingCount - offset : offset;
            source.Append("    public static T Step");
            source.Append(index);
            source.Append("<T>() where T : new() => ");
            if (index == forwardingCount)
            {
                source.AppendLine("new T();");
            }
            else
            {
                source.Append("Step");
                source.Append(index + 1);
                source.AppendLine("<T>();");
            }
        }

        source.AppendLine("}");
        AppendKernel(source, "GenericFactory.Step0<DangerousConstructed>();");
        return source.ToString();
    }

    public static string OrdinaryChain(int helperCount)
    {
        var source = StartSource();
        source.AppendLine("public static class OrdinaryFactory");
        source.AppendLine("{");
        for (var index = 0; index < helperCount; index++)
        {
            source.Append("    public static DangerousConstructed Step");
            source.Append(index);
            source.Append("() => ");
            if (index + 1 == helperCount)
            {
                source.AppendLine("new DangerousConstructed();");
            }
            else
            {
                source.Append("Step");
                source.Append(index + 1);
                source.AppendLine("();");
            }
        }

        source.AppendLine("}");
        AppendKernel(source, "OrdinaryFactory.Step0();");
        return source.ToString();
    }

    public static CSharpCompilation CreateCompilation(string source)
        => CSharpCompilation.Create(
            "DotBoxDGenericConstructionReachabilityProbe",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            TrustedPlatformReferences()
                .Append(MetadataReference.CreateFromFile(typeof(PluginAttribute).Assembly.Location))
                .Append(MetadataReference.CreateFromFile(typeof(SandboxModule).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static StringBuilder StartSource()
    {
        var source = new StringBuilder();
        source.AppendLine("using DotBoxD.Abstractions;");
        source.AppendLine("public sealed class DangerousConstructed");
        source.AppendLine("{");
        source.AppendLine("    public DangerousConstructed() => System.IO.File.WriteAllText(\"x.txt\", \"bad\");");
        source.AppendLine("}");
        return source;
    }

    private static void AppendKernel(StringBuilder source, string call)
    {
        source.AppendLine("[Plugin(\"generic-construction-probe\")]");
        source.AppendLine("public sealed class ProbeKernel : IEventKernel<string>");
        source.AppendLine("{");
        source.AppendLine("    public bool ShouldHandle(string e, HookContext context)");
        source.AppendLine("    {");
        source.Append("        _ = ");
        source.AppendLine(call);
        source.AppendLine("        return true;");
        source.AppendLine("    }");
        source.AppendLine("    public void Handle(string e, HookContext context) { }");
        source.AppendLine("}");
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(static reference => MetadataReference.CreateFromFile(reference));
    }
}
