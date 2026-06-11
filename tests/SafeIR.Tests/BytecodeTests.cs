using SafeIR;

namespace SafeIR.Tests;

public sealed class BytecodeTests
{
    [Fact]
    public async Task Bytecode_lowering_is_stable_for_semantically_equal_json()
    {
        var host = SandboxTestHost.Create();
        var first = await host.ParseJsonAsync(SandboxTestHost.PureScoreJson());
        var second = await host.ParseJsonAsync("""
        {
          "functions": [
            {
              "body": [
                { "value": { "right": { "i32": 10 }, "left": { "var": "level" }, "op": "mul" }, "name": "base", "op": "set" },
                { "name": "bonus", "op": "set", "value": { "left": { "var": "rarity" }, "op": "mul", "right": { "i32": 25 } } },
                { "op": "return", "value": { "op": "add", "left": { "var": "base" }, "right": { "var": "bonus" } } }
              ],
              "returnType": "I32",
              "parameters": [
                { "type": "I32", "name": "level" },
                { "type": "I32", "name": "rarity" }
              ],
              "visibility": "entrypoint",
              "id": "main"
            }
          ],
          "capabilityRequests": [],
          "targetSandboxVersion": "1.0.0",
          "version": "1.0.0",
          "id": "loot-score"
        }
        """);
        var policy = SandboxPolicyBuilder.Create().Build();

        var firstPlan = await host.PrepareAsync(first, policy);
        var secondPlan = await host.PrepareAsync(second, policy);

        Assert.Equal(
            Fingerprint(firstPlan.Bytecode.Functions["main"].Instructions),
            Fingerprint(secondPlan.Bytecode.Functions["main"].Instructions));
    }

    [Fact]
    public async Task Control_flow_lowers_to_explicit_jumps()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync("""
        {
          "id": "sum-loop",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "set", "name": "sum", "value": { "i32": 0 } },
                {
                  "op": "forRange",
                  "local": "i",
                  "start": { "i32": 0 },
                  "end": { "i32": 3 },
                  "body": [
                    {
                      "op": "set",
                      "name": "sum",
                      "value": { "op": "add", "left": { "var": "sum" }, "right": { "var": "i" } }
                    }
                  ]
                },
                { "op": "return", "value": { "var": "sum" } }
              ]
            }
          ]
        }
        """);

        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build());
        var ops = plan.Bytecode.Functions["main"].Instructions.Select(i => i.Op).ToArray();

        Assert.Contains(BytecodeOp.JumpIfFalse, ops);
        Assert.Contains(BytecodeOp.Jump, ops);
        Assert.Contains(BytecodeOp.Return, ops);
    }

    private static IReadOnlyList<string> Fingerprint(IReadOnlyList<BytecodeInstruction> instructions)
        => instructions.Select(i => $"{i.Op}:{i.Operand}").ToArray();
}
