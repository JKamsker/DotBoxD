using SafeIR;

namespace SafeIR.Tests;

public sealed class TypeAndLiteralValidationTests
{
    [Theory]
    [InlineData("F32")]
    [InlineData("Decimal")]
    [InlineData("Bytes")]
    [InlineData("Command")]
    public async Task Function_signatures_reject_unsupported_scalar_types(string typeName)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(ModuleReturningType('"' + typeName + '"'));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Theory]
    [InlineData("""{ "name": "Option", "arguments": ["I32"] }""")]
    [InlineData("""{ "name": "Result", "arguments": ["I32", "String"] }""")]
    [InlineData("""{ "name": "Tuple", "arguments": ["I32", "String"] }""")]
    public async Task Function_signatures_reject_unsupported_composite_types(string typeJson)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(ModuleReturningType(typeJson));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-UNKNOWN");
    }

    [Fact]
    public async Task Function_signatures_reject_non_hashable_map_key_types()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(ModuleReturningType(
            """{ "name": "Map", "arguments": [{ "name": "List", "arguments": ["I32"] }, "I32"] }"""));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-TYPE-MAP-KEY");
    }

    [Theory]
    [InlineData("String", """{ "string": "System.IO.File.ReadAllText" }""")]
    [InlineData("String", """{ "string": "0x06000001" }""")]
    [InlineData("String", """{ "string": "IL_0001: calli" }""")]
    [InlineData("SandboxPath", """{ "path": "System.IO.File" }""")]
    [InlineData("SandboxUri", """{ "uri": "https://api.example.com/0x06000001" }""")]
    [InlineData("PlayerId", """{ "playerId": "0x06000001" }""")]
    [InlineData("PlayerId", """{ "playerId": "System.Type" }""")]
    public async Task Literals_reject_clr_and_il_payloads(string returnType, string literalJson)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(ModuleReturning(returnType, literalJson));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-IR-CLR-REF");
    }

    private static string ModuleReturningType(string typeJson)
        => ModuleReturning(typeJson, """{ "i32": 0 }""", typeIsJson: true);

    private static string ModuleReturning(string returnType, string literalJson, bool typeIsJson = false)
    {
        var type = typeIsJson ? returnType : '"' + returnType + '"';
        return $$"""
        {
          "id": "type-and-literal-validation",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": {{type}},
              "body": [{ "op": "return", "value": {{literalJson}} }]
            }
          ]
        }
        """;
    }
}
