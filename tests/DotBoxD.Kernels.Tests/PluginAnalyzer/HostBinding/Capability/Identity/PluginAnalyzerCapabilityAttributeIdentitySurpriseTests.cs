using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding.Capability;

public sealed class PluginAnalyzerCapabilityAttributeIdentitySurpriseTests
{
    [Fact]
    public void Foreign_capability_attribute_does_not_add_a_required_capability()
    {
        var package = PluginAnalyzerGeneratedPackageFactory.CreateWithReferences(
            """
            extern alias foreignCapability;

            using DotBoxD.Abstractions;
            using DotBoxD.Plugins;
            using DotBoxD.Plugins.Runtime;

            namespace Sample;

            public sealed record CapabilityEvent(
                string TargetId,
                string Message,
                [property: foreignCapability::DotBoxD.Abstractions.Capability("event.read.foreign")] int Foreign,
                [property: Capability("event.read.real")] int Real);

            [Plugin("foreign-capability-attribute")]
            public sealed partial class ForeignCapabilityKernel : IEventKernel<CapabilityEvent>
            {
                public bool ShouldHandle(CapabilityEvent e, HookContext ctx) => e.Foreign > 0 && e.Real > 0;

                public void Handle(CapabilityEvent e, HookContext ctx)
                    => ctx.Messages.Send(e.TargetId, e.Message);
            }
            """,
            "Sample.ForeignCapabilityPluginPackage",
            CreateForeignCapabilityReference());

        Assert.DoesNotContain("event.read.foreign", package.Manifest.RequiredCapabilities);
        Assert.Contains("event.read.real", package.Manifest.RequiredCapabilities);
    }

    private static MetadataReference CreateForeignCapabilityReference()
    {
        var compilation = CSharpCompilation.Create(
            "ForeignCapabilityAttribute",
            [CSharpSyntaxTree.ParseText("""
                namespace DotBoxD.Abstractions;

                [System.AttributeUsage(System.AttributeTargets.Property)]
                public sealed class CapabilityAttribute(string id) : System.Attribute
                {
                    public string Id { get; } = id;
                }
                """)],
            TrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var emit = compilation.Emit(assembly);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));

        return MetadataReference.CreateFromImage(
            assembly.ToArray(),
            new MetadataReferenceProperties(MetadataImageKind.Assembly, aliases: ["foreignCapability"]));
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
