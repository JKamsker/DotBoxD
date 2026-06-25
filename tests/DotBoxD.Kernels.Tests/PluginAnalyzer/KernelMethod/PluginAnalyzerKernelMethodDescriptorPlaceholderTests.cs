using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.KernelMethod;

public sealed partial class PluginAnalyzerKernelMethodDescriptorTests
{
    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_arbitrary_parameter_placeholder()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: false,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("I32", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "I32(1)",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedArbitraryPlaceholderKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale parameter metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_rejects_placeholder_inside_literal()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Always",
                "bool Always(string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "StringEquals(Str(\"__dotboxd_kernel_method_arg_0__\"), Str(\"x\"))",
                worldMembers: string.Empty,
                "public bool Always(string id) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedLiteralPlaceholderKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(ShapeConsumerSource("ctx.Always(e.MonsterId)"), sdkReference);

        Assert.Contains(
            diagnostics,
            diagnostic => diagnostic.GetMessage().Contains(
                "stale parameter metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Prebuilt_sdk_descriptor_replacement_does_not_rewrite_inserted_argument_source()
    {
        var sdkReference = CompilePlainReference(
            ShapeDescriptorSdkSource(
                "Matches",
                "bool Matches(string,string)",
                DotBoxDGenerationNames.ManifestTypes.Bool,
                allocates: true,
                capabilities: [],
                effects: [],
                parameters:
                [
                    new("__dotboxd_kernel_method_arg_0__", DotBoxDGenerationNames.ManifestTypes.String),
                    new("__dotboxd_kernel_method_arg_1__", DotBoxDGenerationNames.ManifestTypes.String)
                ],
                "StringEquals(__dotboxd_kernel_method_arg_0__, Str(\"x\"))",
                worldMembers: string.Empty,
                "public bool Matches(string left, string right) => throw new System.NotSupportedException(\"metadata-only descriptor\");"),
            "ForgedSequentialPlaceholderKernelMethodDescriptorSdk");
        var diagnostics = GeneratorDiagnostics(
            ShapeConsumerSource("ctx.Matches(\"__dotboxd_kernel_method_arg_1__\", e.MonsterId)"),
            sdkReference);

        Assert.DoesNotContain(
            diagnostics,
            static diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
    }
}
