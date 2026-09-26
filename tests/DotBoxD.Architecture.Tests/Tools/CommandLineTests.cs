using System.Globalization;
using System.Text.Json;
using DotBoxD.Cli.Infrastructure;

namespace DotBoxD.Architecture.Tests.Tools;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("--help", 0)]
    [InlineData("--version", 0)]
    [InlineData("unknown", 2)]
    public async Task Discovery_and_usage_never_require_interactive_input(string argument, int expected)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(expected, await CommandLine.RunAsync([argument], output, error));
        Assert.NotEmpty(expected == 0 ? output.ToString() : error.ToString());
    }

    [Fact]
    public async Task Machine_errors_keep_the_versioned_envelope()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.Equal(2, await CommandLine.RunAsync(["replay", "missing.dbxtrace", "--backend", "invalid", "--json"], output, error));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("meta").GetProperty("schemaVersion").GetInt32());
        Assert.Equal("input", json.RootElement.GetProperty("error").GetProperty("kind").GetString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task Human_output_escapes_terminal_control_characters()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {"id":"\u001b[31m","version":"1.0.0","functions":[]}
                """);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(1, await CommandLine.RunAsync(["explain", path], output, error));
            Assert.DoesNotContain("\u001b", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Module: \\u001b[31m", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public async Task Non_object_input_returns_a_machine_readable_error(string input)
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, input);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(2, await CommandLine.RunAsync(["explain", path, "--json"], output, error));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.False(json.RootElement.GetProperty("ok").GetBoolean());
            Assert.Empty(error.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Explain_has_distinct_human_and_json_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {"id":"example","version":"1.0.0","targetSandboxVersion":"1.0.0","capabilityRequests":[],
                "functions":[{"id":"main","visibility":"entrypoint","parameters":[],"returnType":"I32",
                "body":[{"op":"return","value":{"i32":42}}]}]}
                """);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            using var error = new StringWriter(CultureInfo.InvariantCulture);
            Assert.Equal(0, await CommandLine.RunAsync(["explain", path], output, error));
            Assert.StartsWith("Module: example", output.ToString(), StringComparison.Ordinal);
            Assert.Contains("Capability | Decision", output.ToString(), StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
            Assert.Equal(0, await CommandLine.RunAsync(["explain", path, "--json"], output, error));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("example", json.RootElement.GetProperty("data").GetProperty("ModuleId").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
