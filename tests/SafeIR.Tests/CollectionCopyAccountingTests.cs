using System.Collections;
using SafeIR;

namespace SafeIR.Tests;

public sealed class CollectionCopyAccountingTests
{
    [Fact]
    public async Task List_add_charges_projected_copy_allocation_before_enumerating_source()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync("""
        {
          "id": "list-copy-accounting",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [
                { "name": "input", "type": { "name": "List", "arguments": ["I32"] } }
              ],
              "returnType": { "name": "List", "arguments": ["I32"] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.add",
                    "args": [{ "var": "input" }, { "i32": 2 }]
                  }
                }
              ]
            }
          ]
        }
        """);
        var input = new ListValue(
            new ThrowingEnumerableList([SandboxValue.FromInt32(1)]),
            SandboxType.I32);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithMaxAllocatedBytes(16).Build());

        var result = await host.ExecuteAsync(plan, "main", input);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.Equal(32, result.ResourceUsage.AllocatedBytes);
    }

    private sealed class ThrowingEnumerableList(IReadOnlyList<SandboxValue> values) : IReadOnlyList<SandboxValue>
    {
        public int Count => values.Count;

        public SandboxValue this[int index] => values[index];

        public IEnumerator<SandboxValue> GetEnumerator()
            => throw new InvalidOperationException("source should not be enumerated after quota denial");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
