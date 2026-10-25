using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Authoring.UiGeneratorFixture;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiRecordStructHandlerSurpriseTests
{
    [Theory]
    [InlineData("public partial record struct")]
    [InlineData("public readonly partial record struct")]
    public void Record_struct_handlers_preserve_declaration_kind_and_public_facets(string declaration)
    {
        var result = Generate($$"""
            using DotBoxD.UI.Authoring;
            {{declaration}} Handlers
            {
                [UiLocalHandler] public static int Increment(int value) => value + 1;
                [UiRemoteHandler(7)] public static string Search(string value) => value;
            }
            """);

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var handlers = result.Output.GetTypeByMetadataName("Handlers")!;
        Assert.True(handlers.IsRecord);
        Assert.Equal(TypeKind.Struct, handlers.TypeKind);
        Assert.Equal(7, Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(handlers.GetMembers("SearchUiEndpoint"))).ConstantValue);
        Assert.Equal("DotBoxD.UI.Authoring.UiKernelDefinition<int, int>",
            Assert.IsAssignableFrom<IMethodSymbol>(Assert.Single(handlers.GetMembers("IncrementUiKernel"))).ReturnType.ToDisplayString());
    }
}
