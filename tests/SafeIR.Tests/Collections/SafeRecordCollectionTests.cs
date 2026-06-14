using SafeIR;

namespace SafeIR.Tests;

/// <summary>
/// Coverage of the sandbox record/object type (Followup #2 foundation): the <c>record.new</c> and
/// <c>record.get</c> intrinsics build and read a positional composite value, a <c>List&lt;Record&gt;</c>
/// (a list of objects) round-trips through the JSON IR and executes, and the validator enforces a
/// constant field index. Records run in the interpreter (the full sandbox semantics); compiled-mode
/// record emission is a separate follow-up, so these execute interpreted via the default test host.
/// </summary>
public sealed class SafeRecordCollectionTests
{
    [Fact]
    public async Task Record_field_is_built_and_read_through_the_interpreter()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "record-build-read",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Bool",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "record.get",
                    "args": [
                      {
                        "call": "record.new",
                        "genericType": { "name": "Record", "arguments": ["I32", "Bool"] },
                        "args": [ { "i32": 7 }, { "bool": true } ]
                      },
                      { "i32": 1 }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, RecordPolicy());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(SandboxValue.FromBool(true), result.Value);
    }

    [Fact]
    public async Task A_list_of_records_round_trips_through_json_and_executes()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "list-of-records",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [ { "name": "monsterId", "type": "I32" } ],
              "returnType": { "name": "List", "arguments": [ { "name": "Record", "arguments": ["I32", "Bool"] } ] },
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "list.of",
                    "args": [
                      {
                        "call": "record.new",
                        "genericType": { "name": "Record", "arguments": ["I32", "Bool"] },
                        "args": [ { "var": "monsterId" }, { "bool": true } ]
                      },
                      {
                        "call": "record.new",
                        "genericType": { "name": "Record", "arguments": ["I32", "Bool"] },
                        "args": [ { "i32": 99 }, { "bool": false } ]
                      }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);
        var plan = await host.PrepareAsync(module, RecordPolicy());

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.FromInt32(42));

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var list = Assert.IsType<ListValue>(result.Value);
        Assert.Equal(2, list.Values.Count);
        Assert.Equal(SandboxType.Record([SandboxType.I32, SandboxType.Bool]), list.ItemType);

        var first = Assert.IsType<RecordValue>(list.Values[0]);
        Assert.Equal([SandboxValue.FromInt32(42), SandboxValue.FromBool(true)], first.Fields);

        var second = Assert.IsType<RecordValue>(list.Values[1]);
        Assert.Equal([SandboxValue.FromInt32(99), SandboxValue.FromBool(false)], second.Fields);
    }

    [Fact]
    public async Task record_get_requires_a_constant_field_index()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ImportJsonAsync("""
        {
          "id": "record-variable-index",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [ { "name": "index", "type": "I32" } ],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "record.get",
                    "args": [
                      {
                        "call": "record.new",
                        "genericType": { "name": "Record", "arguments": ["I32", "I32"] },
                        "args": [ { "i32": 1 }, { "i32": 2 } ]
                      },
                      { "var": "index" }
                    ]
                  }
                }
              ]
            }
          ]
        }
        """);

        var exception = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await host.PrepareAsync(module, RecordPolicy()).AsTask());

        Assert.Contains(exception.Diagnostics, d => d.Code == "E-CALL-RECORD-INDEX");
    }

    private static SandboxPolicy RecordPolicy()
        => SandboxPolicyBuilder.Create()
            .WithFuel(100_000)
            .Build();
}
