using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Support;

internal static class MalformedModuleGraphTestData
{
    private static readonly SourceSpan Span = new(0, 0);

    public static TheoryData<SandboxModule, string> Modules(string nullFunctionModuleId, string fallbackModuleId)
        => new()
        {
            { ModuleWithNullFunctionEntry(nullFunctionModuleId), "functions" },
            { ModuleWithNullParameterEntry(fallbackModuleId), "parameters" },
            { ModuleWithNullReturnExpression(fallbackModuleId), "body" },
            { ModuleWithNullCallArgument(fallbackModuleId), "arguments" }
        };

    public static void AssertMalformedGraphRejected(Exception? exception, string expectedContractName)
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

    private static SandboxModule ModuleWithNullFunctionEntry(string moduleId)
        => new(
            moduleId,
            SemVersion.One,
            SemVersion.One,
            [],
            [null!],
            new Dictionary<string, string>());

    private static SandboxModule ModuleWithNullParameterEntry(string moduleId)
        => ModuleWithFunction(moduleId, new SandboxFunction(
            "main",
            true,
            [null!],
            SandboxType.Unit,
            [ReturnUnit()]));

    private static SandboxModule ModuleWithNullReturnExpression(string moduleId)
        => ModuleWithFunction(moduleId, new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(null!, Span)]));

    private static SandboxModule ModuleWithNullCallArgument(string moduleId)
        => ModuleWithFunction(moduleId, new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.Unit,
            [
                new ReturnStatement(
                    new CallExpression("identity", [null!], SandboxType.Unit, Span),
                    Span)
            ]));

    private static SandboxModule ModuleWithFunction(string moduleId, SandboxFunction function)
        => new(
            moduleId,
            SemVersion.One,
            SemVersion.One,
            [],
            [function],
            new Dictionary<string, string>());

    private static ReturnStatement ReturnUnit()
        => new(new LiteralExpression(SandboxValue.Unit, Span), Span);
}
