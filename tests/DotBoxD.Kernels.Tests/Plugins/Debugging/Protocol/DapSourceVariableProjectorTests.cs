using System.Text.Json;
using DotBoxD.DebugAdapter;

namespace DotBoxD.Kernels.Tests.Plugins.Debugging.Protocol;

public sealed class DapSourceVariableProjectorTests
{
    private static readonly DapSourceVariableBinding[] Bindings =
    [
        new("$dotboxd.event", "e", "MonsterAggroEvent", "{MonsterAggroEvent}"),
        new("e_MonsterId", "e.MonsterId", null, null),
        new("e_Distance", "e.Distance", null, null),
        new("$dotboxd.context", "ctx", "HookContext", "{HookContext}"),
        new("$dotboxd.context.messages", "ctx.Messages", "IPluginMessageSink", "<host capability proxy>"),
        new(
            "$dotboxd.context.cancellationToken",
            "ctx.CancellationToken",
            "CancellationToken",
            "<execution cancellation token>")
    ];

    [Fact]
    public void Maps_lowered_slots_to_expandable_authored_values()
    {
        var variables = Variables(
            ("e_MonsterId", "String", JsonSerializer.SerializeToElement(new { type = "String", value = "monster-1" })),
            ("e_Distance", "I32", JsonSerializer.SerializeToElement(new { type = "I32", value = 3 })));

        var projected = DapSourceVariableProjector.Map(variables, Bindings, includeSynthetic: true);
        var authoredEvent = Assert.Single(projected.EnumerateArray(), variable =>
            variable.GetProperty("name").GetString() == "e");
        Assert.Equal("MonsterAggroEvent", authoredEvent.GetProperty("type").GetString());
        Assert.Equal("MonsterAggroEvent", authoredEvent.GetProperty("value").GetProperty("type").GetString());
        var children = authoredEvent.GetProperty("value").GetProperty("children").EnumerateArray().ToArray();

        Assert.Equal(["MonsterId", "Distance"], children.Select(child => child.GetProperty("name").GetString()));
        var context = Assert.Single(projected.EnumerateArray(), variable =>
            variable.GetProperty("name").GetString() == "ctx");
        var contextChildren = context.GetProperty("value").GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(
            ["Messages", "CancellationToken"],
            contextChildren.Select(child => child.GetProperty("name").GetString()));
    }

    [Fact]
    public void Evaluates_authored_roots_locally_and_translates_member_expressions_for_the_server()
    {
        var arguments = Variables(
            ("e_MonsterId", "String", JsonSerializer.SerializeToElement(new { type = "String", value = "monster-1" })),
            ("e_Distance", "I32", JsonSerializer.SerializeToElement(new { type = "I32", value = 3 })));

        Assert.True(DapSourceVariableProjector.TryEvaluate(
            arguments,
            JsonSerializer.SerializeToElement(Array.Empty<object>()),
            Bindings,
            "e",
            out var value));
        Assert.Equal("MonsterAggroEvent", value.GetProperty("type").GetString());
        Assert.Equal("e_Distance <= 4", DapSourceVariableProjector.Translate("e.Distance <= 4", Bindings));
    }

    private static JsonElement Variables(params (string Name, string Type, JsonElement Value)[] values)
        => JsonSerializer.SerializeToElement(values.Select(value => new
        {
            name = value.Name,
            kind = "Argument",
            type = value.Type,
            assigned = true,
            value = value.Value
        }));
}
