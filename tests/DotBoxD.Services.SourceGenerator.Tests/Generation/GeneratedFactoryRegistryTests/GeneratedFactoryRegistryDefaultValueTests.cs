using DotBoxD.Services.Generated;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedFactoryRegistryTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

public sealed class GeneratedFactoryRegistryDefaultValueTests
{
    [Fact]
    public void GeneratedMetadata_BoxedDefaultsPreserveDefaultLiteralValueTypes()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using DotBoxD.Services.Attributes;

            namespace Metadata.DefaultLiteral
            {
                public enum Status
                {
                    None,
                    Ready
                }

                [DotBoxDService]
                public interface IDefaults
                {
                    Task<int> EchoAsync(
                        int count = default,
                        Status status = default,
                        Guid id = default,
                        DateTime at = default);
                }
            }
            """;

        var assembly = CompileAndLoad(source);
        var serviceType = assembly.GetType("Metadata.DefaultLiteral.IDefaults")!;
        var statusType = assembly.GetType("Metadata.DefaultLiteral.Status")!;
        var generated = assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")
            ?? throw new InvalidOperationException("Generated factory type not found.");
        var services = Assert.IsAssignableFrom<IReadOnlyList<GeneratedService>>(
            generated.GetProperty("Services")!.GetValue(null));

        var service = services.Single(candidate => candidate.ServiceType == serviceType);
        var method = service.Methods.Single(candidate => candidate.Name == "EchoAsync");

        Assert.Equal(0, method.Parameters[0].DefaultValue);
        Assert.Equal(Enum.ToObject(statusType, 0), method.Parameters[1].DefaultValue);
        Assert.Equal(Guid.Empty, method.Parameters[2].DefaultValue);
        Assert.Equal(DateTime.MinValue, method.Parameters[3].DefaultValue);
    }
}
