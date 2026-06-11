namespace SafeIR.Tests;

public sealed class JsonImporterStrictShapeTests
{
    [Fact]
    public void Invalid_json_is_rejected()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("{not json"));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-INVALID");
    }

    [Fact]
    public void Missing_required_root_field_is_rejected()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "missing-functions",
          "version": "1.0.0"
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-MISSING");
    }

    [Fact]
    public void Siftql_remote_code_envelope_is_rejected()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "Version": 1,
          "HostContract": "siftql.host.v1",
          "Program": {}
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Unknown_statement_operator_is_rejected()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "bad-statement",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "throw", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-STATEMENT");
    }
}
