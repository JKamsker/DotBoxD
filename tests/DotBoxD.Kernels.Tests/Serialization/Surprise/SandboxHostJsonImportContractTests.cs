using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class SandboxHostJsonImportContractTests
{
    [Fact]
    public async Task ImportJsonAsync_rejects_null_json_ir_with_facade_parameter_name()
    {
        using var host = SandboxHost.Create();

        var ex = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await host.ImportJsonAsync(null!));

        Assert.Equal("jsonIr", ex.ParamName);
    }

    [Fact]
    public void Importer_rejects_null_json_with_importer_parameter_name()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => JsonImporter.Import(null!));

        Assert.Equal("json", ex.ParamName);
    }
}
