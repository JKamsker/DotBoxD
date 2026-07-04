using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Serialization;

public sealed class JsonExporterMalformedGraphTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [MemberData(nameof(MalformedModules))]
    public void Export_rejects_malformed_public_module_graphs(
        SandboxModule module,
        string expectedContractName)
    {
        var exception = Record.Exception(() => JsonExporter.Export(module));

        AssertMalformedGraphRejected(exception, expectedContractName);
    }

    public static TheoryData<SandboxModule, string> MalformedModules()
        => new()
        {
            { ModuleWithNullFunctionEntry(), "functions" },
            { ModuleWithNullParameterEntry(), "parameters" },
            { ModuleWithNullReturnExpression(), "body" },
            { ModuleWithNullCallArgument(), "arguments" }
        };

    private static void AssertMalformedGraphRejected(Exception? exception, string expectedContractName)
    {
        Assert.NotNull(exception);
        Assert.IsNotType<NullReferenceException>(exception);
        Assert.True(
            exception is ArgumentException or SandboxValidationException,
            $"Expected an explicit public-boundary exception, but got {exception.GetType().FullName}: {exception.Message}");

        var message = exception is SandboxValidationException validation
            ? string.Join(" ", validation.Diagnostics.Select(d => d.Message))
            : exception.Message;

        Assert.Contains(expectedContractName, message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("null", message, StringComparison.OrdinalIgnoreCase);
    }

    private static SandboxModule ModuleWithNullFunctionEntry()
        => new(
            "json-null-function",
            SemVersion.One,
            SemVersion.One,
            [],
            [null!],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithNullParameterEntry()
        => ModuleWithFunction(new SandboxFunction(
            "main",
            true,
            [null!],
            SandboxType.Unit,
            [ReturnUnit()]));

    private static SandboxModule ModuleWithNullReturnExpression()
        => ModuleWithFunction(new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(null!, Span)]));

    private static SandboxModule ModuleWithNullCallArgument()
        => ModuleWithFunction(new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.Unit,
            [
                new ReturnStatement(
                    new CallExpression("identity", [null!], SandboxType.Unit, Span),
                    Span)
            ]));

    private static SandboxModule ModuleWithFunction(SandboxFunction function)
        => new(
            "json-malformed",
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string>());

    private static ReturnStatement ReturnUnit()
        => new(new LiteralExpression(SandboxValue.Unit, Span), Span);
}
