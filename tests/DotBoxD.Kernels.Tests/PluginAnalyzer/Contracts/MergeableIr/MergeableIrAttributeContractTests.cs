namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class MergeableIrAttributeContractTests
{
    [Fact]
    public void Lower_to_ir_attribute_accepts_defined_step_kinds()
    {
        Assert.Equal(
            LoweredPipelineStepKind.Filter,
            new LowerToIrAttribute(LoweredPipelineStepKind.Filter).Kind);
        Assert.Equal(
            LoweredPipelineStepKind.Projection,
            new LowerToIrAttribute(LoweredPipelineStepKind.Projection).Kind);
    }

    [Fact]
    public void Ir_body_of_attribute_defaults_to_projection()
    {
        var attribute = new IRBodyOfAttribute("predicate");

        Assert.Equal("predicate", attribute.ParameterName);
        Assert.Equal(LoweredPipelineStepKind.Projection, attribute.StepKind);
        Assert.False(attribute.HasExplicitStepKind);
    }

    [Fact]
    public void Ir_body_of_attribute_accepts_defined_explicit_step_kinds()
    {
        var attribute = new IRBodyOfAttribute("predicate", LoweredPipelineStepKind.Filter);

        Assert.Equal("predicate", attribute.ParameterName);
        Assert.Equal(LoweredPipelineStepKind.Filter, attribute.StepKind);
        Assert.True(attribute.HasExplicitStepKind);
    }

    [Fact]
    public void Ir_body_of_attribute_rejects_null_parameter_name()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new IRBodyOfAttribute(null!));
        var explicitException = Assert.Throws<ArgumentNullException>(
            () => new IRBodyOfAttribute(null!, LoweredPipelineStepKind.Filter));

        Assert.Equal("parameterName", exception.ParamName);
        Assert.Equal("parameterName", explicitException.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Ir_body_of_attribute_rejects_blank_parameter_name(string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(() => new IRBodyOfAttribute(parameterName));
        var explicitException = Assert.Throws<ArgumentException>(
            () => new IRBodyOfAttribute(parameterName, LoweredPipelineStepKind.Filter));

        Assert.Equal("parameterName", exception.ParamName);
        Assert.Equal("parameterName", explicitException.ParamName);
    }

    [Fact]
    public void Lower_to_ir_attribute_rejects_undefined_step_kind()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new LowerToIrAttribute((LoweredPipelineStepKind)99));

        Assert.Equal("kind", exception.ParamName);
    }

    [Fact]
    public void Ir_body_of_attribute_rejects_undefined_step_kind()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(
            () => new IRBodyOfAttribute("predicate", (LoweredPipelineStepKind)99));

        Assert.Equal("stepKind", exception.ParamName);
    }
}
