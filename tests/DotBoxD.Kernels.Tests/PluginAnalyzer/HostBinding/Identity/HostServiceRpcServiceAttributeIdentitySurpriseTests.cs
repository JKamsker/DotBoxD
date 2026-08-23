using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.HostBinding;

public sealed class HostServiceRpcServiceAttributeIdentitySurpriseTests
{
    [Fact]
    public void AddBindingsFrom_ignores_foreign_lookalike_RpcService_attributes()
    {
        var assembly = CompileForeignServiceAssembly();
        var rootContract = assembly.GetType("Foreign.IRootService", throwOnError: true)!;
        var implementation = Activator.CreateInstance(
            assembly.GetType("Foreign.RootService", throwOnError: true)!)!;
        var builder = new SandboxHostBuilder();

        typeof(HostServiceBindingExtensions)
            .GetMethod(nameof(HostServiceBindingExtensions.AddBindingsFrom))!
            .MakeGenericMethod(rootContract)
            .Invoke(null, [builder, implementation]);

        var rootType = implementation.GetType();
        Assert.Equal(0, rootType.GetProperty("ForeignGetterCalls")!.GetValue(null));
        Assert.Equal(1, rootType.GetProperty("GenuineGetterCalls")!.GetValue(null));
    }

    private static Assembly CompileForeignServiceAssembly()
    {
        const string source = """
            extern alias GenuineServices;

            namespace DotBoxD.Services.Attributes
            {
                [System.AttributeUsage(System.AttributeTargets.Interface)]
                public sealed class RpcServiceAttribute : System.Attribute;
            }

            namespace Foreign
            {
                [DotBoxD.Services.Attributes.RpcService]
                public interface IForeignChildService;

                [GenuineServices::DotBoxD.Services.Attributes.RpcService]
                public interface IGenuineChildService;

                public interface IRootService
                {
                    IForeignChildService ForeignChild { get; }

                    IGenuineChildService GenuineChild { get; }
                }

                public sealed class RootService : IRootService
                {
                    public static int ForeignGetterCalls { get; private set; }

                    public static int GenuineGetterCalls { get; private set; }

                    public IForeignChildService ForeignChild
                    {
                        get
                        {
                            ForeignGetterCalls++;
                            return new ForeignChildService();
                        }
                    }

                    public IGenuineChildService GenuineChild
                    {
                        get
                        {
                            GenuineGetterCalls++;
                            return new GenuineChildService();
                        }
                    }
                }

                internal sealed class ForeignChildService : IForeignChildService;

                internal sealed class GenuineChildService : IGenuineChildService;
            }
            """;

        var servicesReference = MetadataReference.CreateFromFile(
            typeof(DotBoxD.Services.Attributes.RpcServiceAttribute).Assembly.Location)
            .WithProperties(MetadataReferenceProperties.Assembly.WithAliases(
                ImmutableArray.Create("GenuineServices")));
        var compilation = CSharpCompilation.Create(
            "ForeignRpcServiceAttributeIdentityProbe",
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences().Append(servicesReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return Assembly.Load(stream.ToArray());
    }

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        return references.Select(reference => MetadataReference.CreateFromFile(reference));
    }
}
