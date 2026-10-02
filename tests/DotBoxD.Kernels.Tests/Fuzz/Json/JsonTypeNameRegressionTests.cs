using System.Text.Json;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Fuzz.Json;

public sealed class JsonTypeNameRegressionTests
{
    [Theory]
    [InlineData("\"Record\"")]
    [InlineData("{\"name\":\"Record\"}")]
    [InlineData("{\"name\":\"Record\",\"arguments\":[]}")]
    public void Empty_record_types_report_diagnostics(string type)
    {
        var json = $$$"""{"id":"record","version":"1.0.0","functions":[{"id":"main","returnType":{{{type}}},"body":[]}]}""";
        var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
        Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-TYPE");
    }

    [Fact]
    public void Nonempty_record_types_remain_importable()
    {
        const string json = """{"id":"record","version":"1.0.0","functions":[{"id":"main","returnType":{"name":"Record","arguments":["I32"]},"body":[]}]}""";
        var function = Assert.Single(JsonImporter.Import(json).Functions);
        Assert.Equal("Record", function.ReturnType.Name);
        Assert.Single(function.ReturnType.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2000")]
    public void Blank_type_names_report_diagnostics_in_all_type_positions(string name)
    {
        var scalar = JsonSerializer.Serialize(name);
        string[] types = [scalar, $$$"""{"name":{{{scalar}}}}""", $$$"""{"name":"List","arguments":[{{{scalar}}}]}"""];
        foreach (var type in types)
        {
            string[] functions =
            [
                $$$"""{"id":"main","returnType":{{{type}}},"body":[]}""",
                $$$"""{"id":"main","parameters":[{"name":"x","type":{{{type}}}}],"returnType":"I32","body":[]}""",
                $$$"""{"id":"main","returnType":"I32","body":[{"op":"return","value":{"call":"f","genericType":{{{type}}},"args":[]}}]}"""
            ];
            foreach (var function in functions)
            {
                var json = $$$"""{"id":"types","version":"1.0.0","functions":[{{{function}}}]}""";
                var exception = Assert.Throws<SandboxValidationException>(() => JsonImporter.Import(json));
                Assert.Contains(exception.Diagnostics, diagnostic => diagnostic.Code == "E-JSON-TYPE");
            }
        }
    }
}
