namespace SafeIR.Tests;

public sealed class JsonDuplicatePropertyTests
{
    [Fact]
    public void Module_rejects_duplicate_properties()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "first",
          "id": "second",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }

    [Fact]
    public void Metadata_rejects_duplicate_properties()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => SafeIrJsonImporter.Import("""
        {
          "id": "metadata-dupe",
          "version": "1.0.0",
          "metadata": {
            "tag": "a",
            "tag": "b"
          },
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 1 } }]
            }
          ]
        }
        """));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-JSON-SCHEMA");
    }
}
