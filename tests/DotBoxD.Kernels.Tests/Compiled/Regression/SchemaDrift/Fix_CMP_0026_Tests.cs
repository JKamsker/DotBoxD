namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

public sealed class Fix_CMP_0026_Tests
{
    [Fact]
    public void Drift_guard_rejects_same_property_set_when_required_properties_are_relaxed()
    {
        var contract = new JsonSchemaObjectContract(
            "plugin manifest",
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions", "requiredCapabilities", "rpcEntrypoint"],
            ["pluginId", "contract", "mode", "effects", "liveSettings", "subscriptions"]);

        var failures = JsonSchemaDriftGuard.SemanticDriftMessages(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "required": ["pluginId"],
              "properties": {
                "pluginId": { "type": "string" },
                "contract": { "type": "string" },
                "mode": { "type": "string" },
                "effects": { "type": "array" },
                "liveSettings": { "type": "array" },
                "subscriptions": { "type": "array" },
                "requiredCapabilities": { "type": "array" },
                "rpcEntrypoint": { "type": "string" }
              }
            }
            """,
            contract);

        Assert.Contains(failures, failure => failure.Contains("required", StringComparison.Ordinal));
    }

    [Fact]
    public void Drift_guard_rejects_same_property_set_when_statement_discriminator_const_drifts()
    {
        var contract = new JsonSchemaObjectContract(
            "expression statement",
            ["op", "value"],
            ["op", "value"])
        {
            ConstProperties = new Dictionary<string, string> { ["op"] = "expr" }
        };

        var failures = JsonSchemaDriftGuard.SemanticDriftMessages(
            """
            {
              "oneOf": [
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["op", "value"],
                  "properties": {
                    "op": { "const": "return" },
                    "value": { "$ref": "#/$defs/expression" }
                  }
                }
              ]
            }
            """,
            contract);

        Assert.Contains(failures, failure => failure.Contains("const", StringComparison.Ordinal));
    }
}
