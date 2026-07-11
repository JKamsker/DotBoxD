using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingDescriptorRequiredFieldContractTests
{
    [Theory]
    [InlineData("Version", "Version")]
    [InlineData("Parameters", "Parameters")]
    [InlineData("ParameterElement", "Parameters")]
    [InlineData("ReturnType", "ReturnType")]
    [InlineData("CostModel", "CostModel")]
    [InlineData("Invoke", "Invoke")]
    [InlineData("Compiled", "Compiled")]
    public void Binding_registry_rejects_null_required_descriptor_field_values(
        string malformedField,
        string expectedContractName)
    {
        var ex = Record.Exception(() => Build(MalformedDescriptor(malformedField)));

        Assert.NotNull(ex);
        Assert.False(
            ex is NullReferenceException,
            $"Expected public validation for malformed BindingDescriptor.{expectedContractName}, but got {ex.GetType().FullName}.");
        Assert.True(
            IsClosedRequiredFieldFailure(ex, expectedContractName),
            $"Expected ArgumentException naming BindingDescriptor.{expectedContractName} or a binding validation diagnostic, but got {Describe(ex)}.");
    }

    private static BindingRegistry Build(BindingDescriptor descriptor)
        => new BindingRegistryBuilder().Add(descriptor).Build();

    private static BindingDescriptor MalformedDescriptor(string field)
    {
        var descriptor = ValidDescriptor();
        return field switch
        {
            "Version" => descriptor with { Version = null! },
            "Parameters" => descriptor with { Parameters = null! },
            "ParameterElement" => descriptor with { Parameters = [null!] },
            "ReturnType" => descriptor with { ReturnType = null! },
            "CostModel" => descriptor with { CostModel = null! },
            "Invoke" => descriptor with { Invoke = null! },
            "Compiled" => descriptor with { Compiled = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown malformed field.")
        };
    }

    private static bool IsClosedRequiredFieldFailure(Exception ex, string expectedContractName)
    {
        if (ex is ArgumentException argumentException)
        {
            return string.Equals(argumentException.ParamName, expectedContractName, StringComparison.Ordinal);
        }

        if (ex is not SandboxValidationException validationException)
        {
            return false;
        }

        return validationException.Diagnostics.Any(d =>
            d.Message.Contains("descriptor", StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains(expectedContractName, StringComparison.OrdinalIgnoreCase) &&
            d.Message.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(Exception ex)
    {
        if (ex is ArgumentException argumentException)
        {
            return $"{ex.GetType().FullName} ParamName={argumentException.ParamName}";
        }

        if (ex is SandboxValidationException validationException)
        {
            return string.Join(", ", validationException.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        }

        return ex.GetType().FullName ?? ex.GetType().Name;
    }

    private static BindingDescriptor ValidDescriptor()
        => new(
            "test.binding",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
