using DotBoxD.Kernels.Tests.Support;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class JsonExporterMalformedGraphTests
{
    [Theory]
    [MemberData(nameof(MalformedModules))]
    public void Export_rejects_malformed_public_module_graphs(
        SandboxModule module,
        string expectedContractName)
    {
        var exception = Record.Exception(() => JsonExporter.Export(module));

        AssertMalformedGraphRejected(exception, expectedContractName);
    }

    public static TheoryData<SandboxModule, string> MalformedModules()
        => MalformedModuleGraphTestData.Modules("json-null-function", "json-malformed");

    private static void AssertMalformedGraphRejected(Exception? exception, string expectedContractName)
        => MalformedModuleGraphTestData.AssertMalformedGraphRejected(exception, expectedContractName);
}
