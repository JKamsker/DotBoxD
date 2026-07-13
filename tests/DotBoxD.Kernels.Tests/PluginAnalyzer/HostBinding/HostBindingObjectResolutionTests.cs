using DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;
using DotBoxD.Kernels.Sandbox;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostBindingObjectResolutionTests
{
    private const string Source = """
        using DotBoxD.Abstractions;
        using DotBoxD.Kernels.Sandbox;

        namespace Sample;

        [HostBindingObject(
            "host.player",
            "player.read.default",
            SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        public sealed record PlayerContext(string Id)
        {
            public bool Matches(int value) => true;

            [HostBinding("player.read.name", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
            public bool Matches(string value) => true;

            [HostBindingIgnore]
            public bool LocalOnly() => true;

            private bool PrivateHelper() => true;

            public static bool StaticHelper() => true;

            public bool GenericHelper<T>() => true;
        }
        """;

    [Fact]
    public void Public_methods_inherit_defaults_and_overloads_get_distinct_routes()
    {
        var compilation = CreateCompilation();
        var methods = PlayerContext(compilation).GetMembers("Matches").OfType<IMethodSymbol>().ToArray();
        var integer = Resolve(methods.Single(method => method.Parameters[0].Type.SpecialType == SpecialType.System_Int32), compilation);
        var text = Resolve(methods.Single(method => method.Parameters[0].Type.SpecialType == SpecialType.System_String), compilation);

        Assert.Equal("host.player.Matches.i32", integer.BindingId);
        Assert.Equal("player.read.default", integer.Capability);
        Assert.Equal("host.player.Matches.string", text.BindingId);
        Assert.Equal("player.read.name", text.Capability);
    }

    [Theory]
    [InlineData("LocalOnly")]
    [InlineData("PrivateHelper")]
    [InlineData("StaticHelper")]
    [InlineData("GenericHelper")]
    [InlineData("ToString")]
    public void Ignored_and_ineligible_methods_do_not_resolve_as_bindings(string methodName)
    {
        var compilation = CreateCompilation();
        var method = Assert.Single(PlayerContext(compilation).GetMembers(methodName).OfType<IMethodSymbol>());

        Assert.Null(DotBoxDHostBindingExpressionLowerer.HostBinding(method, compilation));
    }

    private static (string BindingId, string? Capability, IReadOnlyList<string> Effects, bool IsAsync) Resolve(
        IMethodSymbol method,
        Compilation compilation)
        => DotBoxDHostBindingExpressionLowerer.HostBinding(method, compilation)
            ?? throw new Xunit.Sdk.XunitException($"Method '{method.Name}' did not resolve as a host binding.");

    private static INamedTypeSymbol PlayerContext(Compilation compilation)
        => compilation.GetTypeByMetadataName("Sample.PlayerContext")
            ?? throw new Xunit.Sdk.XunitException("PlayerContext was not compiled.");

    private static CSharpCompilation CreateCompilation()
    {
        var platformReferences = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => MetadataReference.CreateFromFile(path)) ?? [];
        var references = platformReferences
            .Append(MetadataReference.CreateFromFile(typeof(HostBindingObjectAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(SandboxEffect).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "HostBindingObjectResolutionTests",
            [CSharpSyntaxTree.ParseText(Source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return compilation;
    }
}
