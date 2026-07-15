using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class LoweredPipelineCarrierValidationTests
{
    [Fact]
    public void Composer_rejects_null_step_entries()
    {
        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-step", [null!], SandboxType.I32)));

        Assert.Equal("Steps", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_value()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            Value = null!
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-value", [step], SandboxType.I32)));

        Assert.Equal("Value", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_parameter_entries()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            Parameters = [null!]
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-parameter", [step], SandboxType.I32)));

        Assert.Equal("Parameters", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_parameter_name()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            Parameters = [new Parameter("$dotboxd.current", SandboxType.I32) with { Name = null! }]
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-parameter-name", [step], SandboxType.I32)));

        Assert.Equal("Name", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_parameter_type()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            Parameters = [new Parameter("$dotboxd.current", SandboxType.I32) with { Type = null! }]
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-parameter-type", [step], SandboxType.I32)));

        Assert.Equal("Type", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_input_type()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            InputType = null!
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-input-type", [step], SandboxType.I32)));

        Assert.Equal("InputType", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_scalar_input_tag_parameter_type_mismatches()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "string", "bool", SandboxType.I32);

        var exception = Assert.Throws<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("input-type-mismatch", [step], SandboxType.String)));

        Assert.Contains("parameter type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_rejects_non_scalar_input_tag_parameter_type_mismatches()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "record", "bool", SandboxType.I32);

        var exception = Assert.Throws<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("record-input-type-mismatch", [step], SandboxType.I32)));

        Assert.Contains("InputType", exception.Message, StringComparison.Ordinal);
        Assert.Contains("parameter type", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Composer_accepts_non_scalar_input_tag_when_parameter_type_matches()
    {
        var recordType = SandboxType.Record([SandboxType.I32, SandboxType.String]);
        var step = Step(LoweredPipelineStepKind.Filter, "record", "bool", recordType);

        var module = LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("record-input-type-match", [step], recordType));

        Assert.Contains(module.Functions, function => function.Id == "ShouldHandle" &&
            function.Parameters.Single().Type == recordType);
        Assert.Contains(module.Functions, function => function.Id == "Handle" &&
            function.ReturnType == recordType);
    }

    [Fact]
    public void Composer_rejects_filter_steps_with_non_boolean_output_tags()
    {
        var validFilter = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32);

        var validModule = LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("valid-filter-output", [validFilter], SandboxType.I32));

        Assert.Contains(validModule.Functions, function => function.Id == "ShouldHandle" &&
            function.ReturnType == SandboxType.Bool);
        Assert.Contains(validModule.Functions, function => function.Id == "Handle" &&
            function.ReturnType == SandboxType.I32);

        var malformedFilter = Step(LoweredPipelineStepKind.Filter, "i32", "string", SandboxType.I32);

        var exception = Assert.Throws<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("bad-filter-output", [malformedFilter], SandboxType.I32)));

        Assert.True(
            string.Equals(exception.ParamName, "OutputType", StringComparison.Ordinal) ||
            exception.Message.Contains("bool", StringComparison.Ordinal),
            $"Expected the exception to identify OutputType or require bool output tags, but got: {exception}");
    }

    [Fact]
    public void Composer_rejects_projection_steps_with_null_output_type()
    {
        var step = Step(LoweredPipelineStepKind.Projection, "i32", "string", SandboxType.I32) with
        {
            OutputType = null!
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-output-type", [step], SandboxType.String)));

        Assert.Equal("OutputType", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_required_capability_entries()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            RequiredCapabilities = [null!]
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-required-capability", [step], SandboxType.I32)));

        Assert.Equal("RequiredCapabilities", exception.ParamName);
    }

    [Fact]
    public void Composer_rejects_steps_with_null_effect_entries()
    {
        var step = Step(LoweredPipelineStepKind.Filter, "i32", "bool", SandboxType.I32) with
        {
            Effects = [null!]
        };

        var exception = Assert.ThrowsAny<ArgumentException>(() => LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("null-effect", [step], SandboxType.I32)));

        Assert.Equal("Effects", exception.ParamName);
    }

    private static LoweredPipelineStep Step(
        LoweredPipelineStepKind kind,
        string inputTag,
        string outputTag,
        SandboxType inputType)
    {
        var span = new SourceSpan(1, 1);
        Expression value = kind == LoweredPipelineStepKind.Filter
            ? new LiteralExpression(SandboxValue.FromBool(true), span)
            : new VariableExpression("$dotboxd.current", span);
        return new LoweredPipelineStep(
            kind,
            inputTag,
            outputTag,
            [new Parameter("$dotboxd.current", inputType)],
            [],
            value,
            [],
            []);
    }
}
