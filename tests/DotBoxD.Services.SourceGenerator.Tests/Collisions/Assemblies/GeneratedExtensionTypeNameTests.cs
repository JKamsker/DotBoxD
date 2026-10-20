using DotBoxD.CodeGeneration.Services;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public sealed class GeneratedExtensionTypeNameTests
{
    [Theory]
    [InlineData("Contracts.A", "Contracts_002eA")]
    [InlineData("Contracts-A", "Contracts_002dA")]
    [InlineData("Contracts A", "Contracts_0020A")]
    [InlineData("Contracts.Å", "Contracts_002e_00c5")]
    public void Assembly_names_remain_distinct_from_literal_escape_sequences(string name, string literal)
    {
        Assert.Equal("DotBoxDGeneratedExtensions_" + literal, GeneratedServiceTypeNames.Extensions(name));
        Assert.NotEqual(GeneratedServiceTypeNames.Extensions(name), GeneratedServiceTypeNames.Extensions(literal));
    }
}
