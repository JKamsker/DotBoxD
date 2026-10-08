using DotBoxD.Plugins.Analyzer.Analysis.UI;
using DotBoxD.UI;
using DotBoxD.UI.Razor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DotBoxD.Kernels.Tests.UI.Razor;

internal static class UiRazorFixture
{
    public const string Component = """
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        [UiRazorComponent("Counter.ui.razor")]
        public partial class Counter
        {
            [UiLocalHandler] public static int Increment(int value) => value + 1;
            [UiLocalHandler] public static string Label(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            [UiRemoteHandler(7)] public static string Search(string value) => System.IO.File.ReadAllText(value);
            public static UiPackage Package()
            {
                var builder = new UiBuilder();
                return builder.Build(new Counter().Render(builder));
            }
        }
        """;
    public const string Markup = """
        @state int count = 0;
        @kernel label = Ui.Bind(Label, count);
        @kernel increment = Ui.Bind(Increment, count);
        <UiVertical>
            <UiText Text="@Ui.Bind(label)" />
            <UiButton Text="Increment" OnClick="@Ui.Handle(increment, count)" />
        </UiVertical>
        """;

    public static UiPackage Package(string source = Component, string markup = Markup)
    {
        var result = Generate(source, markup);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return (UiPackage)Authoring.UiGeneratorFixture.Emit(result.Output).GetType("Counter")!.GetMethod("Package")!.Invoke(null, null)!;
    }

    public static Authoring.UiGeneratorFixture.Generation Generate(string source, string markup)
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Append(typeof(UiPackage).Assembly.Location).Distinct(StringComparer.Ordinal)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("UiRazor" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new UiHandlerGenerator().AsSourceGenerator(), new UiRazorGenerator().AsSourceGenerator()],
            additionalTexts: [new TextFile("/authoring/Counter.ui.razor", markup)],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        return new(driver.GetRunResult(), diagnostics, compilation, output, driver);
    }

    internal sealed class TextFile(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }
}
