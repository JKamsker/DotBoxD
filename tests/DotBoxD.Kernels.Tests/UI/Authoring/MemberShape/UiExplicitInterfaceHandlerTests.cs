using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Authoring.UiGeneratorFixture;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiExplicitInterfaceHandlerTests
{
    [Fact]
    public void Explicit_interface_remote_handlers_emit_valid_endpoint_members()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;

            public interface IRemoteHandlers
            {
                string Search(string value);
            }

            public partial class Handlers : IRemoteHandlers
            {
                [UiRemoteHandler(7)] string IRemoteHandlers.Search(string value) => value;
            }
        """);

        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public const int SearchUiEndpoint = 7;", string.Join("", result.Result.GeneratedTrees), StringComparison.Ordinal);
    }
    [Fact]
    public void Closed_generic_explicit_interface_remote_handler_has_valid_hint_and_endpoint()
    {
        var result = Generate("""
            using DotBoxD.UI.Authoring;
            public interface IRemoteHandlers<T> { T Search(T value); }
            public partial class Handlers : IRemoteHandlers<int>
            {
                [UiRemoteHandler(7)] int IRemoteHandlers<int>.Search(int value) => value;
            }
            """);
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Diagnostics.Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning));
        var handlers = result.Output.GetTypeByMetadataName("Handlers")!;
        Assert.Equal(7, Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(handlers.GetMembers("SearchUiEndpoint"))).ConstantValue);
    }

}
