using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins.Debugging;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Execution;

public sealed class PluginDebugSourceVariableTests
{
    private static readonly KernelDebugInfo DebugInfo = new(
        [],
        [],
        [
            new("Handle", "e_MonsterId", "e.MonsterId"),
            new("Handle", "e_Distance", "e.Distance"),
            new("Handle", "$dotboxd.select.1", "monsterId"),
            new("Handle", "$dotboxd.context", "ctx", typeName: "HookContext", displayValue: "{HookContext}"),
            new("Handle", "$dotboxd.context.messages", "ctx.Messages", typeName: "IPluginMessageSink", displayValue: "<host capability proxy>"),
            new("Handle", "$dotboxd.context.cancellation", "ctx.CancellationToken", typeName: "CancellationToken", displayValue: "<execution cancellation token>")
        ]);

    [Fact]
    public void Authored_event_and_context_are_expandable_while_projection_uses_its_source_name()
    {
        var arguments = Variables(
            ("e_MonsterId", SandboxValue.FromString("monster-1")),
            ("e_Distance", SandboxValue.FromInt32(3)));
        var locals = new[]
        {
            new SandboxDebugVariable(
                "$dotboxd.select.1",
                SandboxType.String,
                SandboxDebugVariableKind.Local,
                true,
                SandboxValue.FromString("monster-1"))
        };

        var mappedArguments = PluginDebugSourceVariables.Map(
            arguments,
            DebugInfo,
            "Handle",
            SandboxDebugVariableKind.Argument);
        var mappedLocals = PluginDebugSourceVariables.Map(
            locals,
            DebugInfo,
            "Handle",
            SandboxDebugVariableKind.Local);

        var e = Assert.Single(mappedArguments, variable => variable.Name == "e");
        Assert.Equal(["MonsterId", "Distance"], e.Value!.Children!.Select(child => child.Name));
        var context = Assert.Single(mappedArguments, variable => variable.Name == "ctx");
        Assert.Equal(["Messages", "CancellationToken"], context.Value!.Children!.Select(child => child.Name));
        var monsterId = Assert.Single(mappedLocals);
        Assert.Equal("monsterId", monsterId.Name);
        Assert.Equal("monster-1", monsterId.Value!.Value);
    }

    [Theory]
    [InlineData("e.MonsterId", "monster-1")]
    [InlineData("monsterId", "monster-1")]
    [InlineData("ctx.Messages", "<host capability proxy>")]
    public void Authored_expressions_evaluate_to_mapped_or_synthetic_values(string expression, string expected)
    {
        var variables = Variables(
            ("e_MonsterId", SandboxValue.FromString("monster-1")),
            ("e_Distance", SandboxValue.FromInt32(3)))
            .Append(new SandboxDebugVariable(
                "$dotboxd.select.1",
                SandboxType.String,
                SandboxDebugVariableKind.Local,
                true,
                SandboxValue.FromString("monster-1")))
            .ToArray();

        Assert.True(PluginDebugSourceVariables.TryEvaluate(variables, DebugInfo, "Handle", expression, out var value));
        Assert.Equal(expected, value!.Value);
    }

    [Fact]
    public void Completion_paths_include_authored_roots_and_members_but_not_runtime_slots()
    {
        var paths = PluginDebugSourceVariables.CompletionPaths(DebugInfo, "Handle");

        Assert.Contains("e.MonsterId", paths);
        Assert.Contains("monsterId", paths);
        Assert.Contains("ctx.Messages", paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("$dotboxd", StringComparison.Ordinal));
    }

    private static SandboxDebugVariable[] Variables(params (string Name, SandboxValue Value)[] values)
        => values.Select(item => new SandboxDebugVariable(
            item.Name,
            item.Value.Type,
            SandboxDebugVariableKind.Argument,
            true,
            item.Value)).ToArray();
}
