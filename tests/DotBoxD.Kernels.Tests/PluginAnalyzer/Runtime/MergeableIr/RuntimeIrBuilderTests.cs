using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

public sealed class RuntimeIrBuilderTests
{
    [Fact]
    public async Task Builder_steps_compose_and_execute()
    {
        var builder = IRBuilder.For<RuntimeBuilderEvent>();
        var filter = builder.FilterStep(
            ir => ir.GreaterThanOrEqual(ir.Field(0), ir.Int32(4)),
            requiredCapabilities: ["event.read.distance"],
            effects: ["cpu"]);
        var projection = builder.ProjectionStep<string>(ir => ir.Field(1));

        var module = LoweredPipelineComposer.Compose(
            new LoweredPipelineComposition("runtime-builder", [filter, projection], SandboxType.String));

        var host = SandboxTestHost.Create();
        var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create()
            .Grant("event.read.distance", new { })
            .WithFuel(1_000_000)
            .Build());

        var gateTrue = await host.ExecuteAsync(plan, "ShouldHandle", Record(5, "target-1"));
        var gateFalse = await host.ExecuteAsync(plan, "ShouldHandle", Record(3, "target-2"));
        var projected = await host.ExecuteAsync(plan, "Handle", Record(5, "target-1"));

        Assert.True(gateTrue.Succeeded, gateTrue.Error?.SafeMessage);
        Assert.True(((BoolValue)gateTrue.Value!).Value);
        Assert.False(((BoolValue)gateFalse.Value!).Value);
        Assert.Equal("target-1", ((StringValue)projected.Value!).Value);
        Assert.Equal("event.read.distance", module.Metadata["dotboxd.requiredCapabilities"]);
        Assert.Equal("cpu", module.Metadata["dotboxd.effects"]);
    }

    [Fact]
    public void Builder_returns_irfunc_carriers_with_generator_compatible_shape_tags()
    {
        var filter = IRBuilder.For<RuntimeBuilderEvent>()
            .Filter(ir => ir.LessThan(ir.Field(0), ir.Int32(10)));
        var projection = IRBuilder.For<RuntimeBuilderEvent>()
            .Projection<string>(ir => ir.Field(1));

        Assert.Equal(LoweredPipelineStepKind.Filter, filter.Step.Kind);
        Assert.Equal("record", filter.Step.InputType);
        Assert.Equal("bool", filter.Step.OutputType);
        Assert.Equal(LoweredPipelineStepKind.Projection, projection.Step.Kind);
        Assert.Equal("record", projection.Step.InputType);
        Assert.Equal("string", projection.Step.OutputType);
        Assert.Equal("$dotboxd.current", filter.Step.Parameters.Single().Name);
    }

    [Fact]
    public void Builder_supports_runtime_output_types_for_dynamic_projections()
    {
        var step = IRBuilder.For<RuntimeBuilderEvent>()
            .ProjectionStep(typeof(string), ir => ir.Field(1));

        Assert.Equal(LoweredPipelineStepKind.Projection, step.Kind);
        Assert.Equal("string", step.OutputType);
    }

    [Fact]
    public void Builder_uses_generator_manifest_tags_for_supported_runtime_shapes()
    {
        AssertInputTag<bool>("bool");
        AssertInputTag<int>("int");
        AssertInputTag<long>("long");
        AssertInputTag<float>("double");
        AssertInputTag<string>("string");
        AssertInputTag<Guid>("guid");
        AssertInputTag<WideEnum>("long");
        AssertInputTag<int[]>("list");
        AssertInputTag<Dictionary<string, int>>("map");
        AssertInputTag<DateTime>("record");
    }

    [Fact]
    public void Builder_rejects_non_marshallable_shapes()
    {
        Assert.Throws<NotSupportedException>(() => IRBuilder.For<UnsupportedInput>());
        Assert.Throws<NotSupportedException>(() => IRBuilder.For<RuntimeBuilderEvent>()
            .ProjectionStep(typeof(UnsupportedInput), ir => ir.Current()));
    }

    [Fact]
    public void Expression_builder_copies_call_arguments()
    {
        var ir = new IRExpressionBuilder();
        var arguments = new Expression[] { ir.Int32(1) };
        var call = ir.Call("identity", arguments);

        arguments[0] = ir.Int32(2);

        var literal = Assert.IsType<LiteralExpression>(call.Arguments.Single());
        Assert.Equal(1, ((I32Value)literal.Value).Value);
    }

    private static SandboxValue Record(int distance, string targetId)
        => SandboxValue.FromRecord([SandboxValue.FromInt32(distance), SandboxValue.FromString(targetId)]);

    private static void AssertInputTag<TInput>(string expected)
        => Assert.Equal(expected, IRBuilder.For<TInput>().FilterStep(ir => ir.Bool(true)).InputType);

    private sealed record RuntimeBuilderEvent(int Distance, string TargetId);

    private sealed class UnsupportedInput
    {
    }

    private enum WideEnum : uint
    {
        Value = 1,
    }
}
