namespace DotBoxD.Kernels.Tests.Compiled.Regression.SchemaDrift;

internal sealed record JsonSchemaObjectContract(
    string Name,
    string[] AllowedProperties,
    string[] RequiredProperties,
    IReadOnlyDictionary<string, string> ConstProperties);
