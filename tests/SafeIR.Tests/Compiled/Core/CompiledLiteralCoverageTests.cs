namespace SafeIR.Tests;

public sealed class CompiledLiteralCoverageTests
{
    public static TheoryData<string, string> LiteralCases()
        => new()
        {
            { "Unit", """{ "unit": true }""" },
            { "I64", """{ "i64": 9223372036854775807 }""" },
            { "SandboxPath", """{ "path": "config/settings.json" }""" },
            { "SandboxUri", """{ "uri": "https://api.example.com/config" }""" },
            { "PlayerId", """{ "playerId": "player-1" }""" }
        };

    [Theory]
    [MemberData(nameof(LiteralCases))]
    public async Task Compiled_literals_match_interpreted_literals(string returnType, string expression)
    {
        var host = SandboxTestHost.Create(compiler: true);
        var module = await host.ImportJsonAsync(ModuleWithReturn(returnType, expression));
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(1_000).Build());

        var interpreted = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        var compiled = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = ExecutionMode.Compiled, AllowFallbackToInterpreter = false });

        Assert.True(interpreted.Succeeded, interpreted.Error?.SafeMessage);
        Assert.True(compiled.Succeeded, compiled.Error?.SafeMessage);
        Assert.Equal(interpreted.Value, compiled.Value);
        Assert.Equal(ExecutionMode.Compiled, compiled.ActualMode);
    }

    private static string ModuleWithReturn(string returnType, string expression)
        => $$"""
        {
          "id": "compiled-literal-coverage",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [{ "op": "return", "value": {{expression}} }]
            }
          ]
        }
        """;
}
