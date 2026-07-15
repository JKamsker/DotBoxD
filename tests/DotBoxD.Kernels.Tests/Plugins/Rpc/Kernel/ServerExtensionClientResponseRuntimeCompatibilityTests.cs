using DotBoxD.Plugins.Analyzer.Analysis.Rpc;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionClientResponseRuntimeCompatibilityTests
{
    [Fact]
    public void Response_emitter_uses_legacy_projection_when_runtime_lacks_SkipValue()
    {
        var compilation = RuntimeCompilation(
            "public ref struct KernelRpcPayloadReader { }");
        var emitter = new RpcKernelClientResponseReadEmitter(compilation);

        var supported = emitter.TryReadExpression(
            compilation.GetSpecialType(SpecialType.System_Int32),
            "response",
            out var expression);

        Assert.False(supported);
        Assert.Empty(expression);
        Assert.Empty(emitter.Helpers);
    }

    [Fact]
    public void Response_emitter_uses_direct_projection_when_runtime_exposes_SkipValue()
    {
        var compilation = RuntimeCompilation(
            "public ref struct KernelRpcPayloadReader { public void SkipValue() { } }");
        var emitter = new RpcKernelClientResponseReadEmitter(compilation);

        var supported = emitter.TryReadExpression(
            compilation.GetSpecialType(SpecialType.System_Int32),
            "response",
            out var expression);

        Assert.True(supported);
        Assert.Equal("KernelRpcResponseReader.Read0(response)", expression);
        Assert.Contains("validator.SkipValue();", emitter.Helpers, StringComparison.Ordinal);
    }

    [Fact]
    public void Response_emitter_rejects_a_generic_SkipValue_lookalike()
    {
        var compilation = RuntimeCompilation(
            "public ref struct KernelRpcPayloadReader { public void SkipValue<T>() { } }");
        var emitter = new RpcKernelClientResponseReadEmitter(compilation);

        var supported = emitter.TryReadExpression(
            compilation.GetSpecialType(SpecialType.System_Int32),
            "response",
            out _);

        Assert.False(supported);
        Assert.Empty(emitter.Helpers);
    }

    [Fact]
    public void Legacy_conversion_reader_avoids_reserved_fixed_helper_name()
    {
        var compilation = RuntimeCompilation(
            "public ref struct KernelRpcPayloadReader { }");
        var dateTime = compilation.GetTypeByMetadataName("System.DateTime")!;
        var emitter = new RpcKernelValueConversionEmitter(
            compilation,
            reservedMemberName: "DateTimeFromWireOffset");

        var expression = emitter.ReadExpression(dateTime, "value");

        Assert.StartsWith("ReadKernelRpcValue", expression, StringComparison.Ordinal);
        Assert.Contains(
            "global::System.DateTime DateTimeFromWireOffset0(global::System.DateTimeOffset value)",
            emitter.Helpers,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "global::System.DateTime DateTimeFromWireOffset(global::System.DateTimeOffset value)",
            emitter.Helpers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Legacy_list_conversion_emits_only_its_required_reader()
    {
        var compilation = RuntimeCompilation(
            "public ref struct KernelRpcPayloadReader { }");
        var listType = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1")!
            .Construct(compilation.GetSpecialType(SpecialType.System_Int32));
        var emitter = new RpcKernelValueConversionEmitter(compilation);

        var expression = emitter.ReadExpression(listType, "value");

        Assert.Equal("ReadKernelRpcValue0(value)", expression);
        Assert.Contains("ReadKernelRpcValue0", emitter.Helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadKernelRpcValue1", emitter.Helpers, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeFromWireOffset", emitter.Helpers, StringComparison.Ordinal);
    }

    private static CSharpCompilation RuntimeCompilation(string readerDeclaration)
        => CSharpCompilation.Create(
            "RuntimeCompatibility",
            [
                CSharpSyntaxTree.ParseText(
                    "namespace DotBoxD.Plugins; " + readerDeclaration,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12))
            ],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
