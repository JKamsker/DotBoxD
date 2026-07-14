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

    [Fact]
    public void Translation_preserves_authored_names_inside_string_literals()
    {
        DapSourceVariableBinding[] bindings = [new("e_Name", "e.Name", null, null)];

        var translated = DapSourceVariableProjector.Translate("e.Name == \"e.Name\"", bindings);

        Assert.Equal("e_Name == \"e.Name\"", translated);
    }

    [Fact]
    public void Translation_preserves_authored_names_inside_character_literals()
    {
        DapSourceVariableBinding[] bindings = [new("e_Initial", "x", null, null)];

        var translated = DapSourceVariableProjector.Translate("x == 'x'", bindings);

        Assert.Equal("e_Initial == 'x'", translated);
    }

    [Fact]
    public void Translation_rewrites_expressions_but_not_text_inside_interpolated_strings()
    {
        DapSourceVariableBinding[] bindings = [new("e_Name", "e.Name", null, null)];

        var translated = DapSourceVariableProjector.Translate(
            "$\"{e.Name}: e.Name\"",
            bindings);

        Assert.Equal("$\"{e_Name}: e.Name\"", translated);
    }

    [Fact]
    public void Direct_projection_uses_its_assigned_source_argument()
    {
        var arguments = Variables(
            ("e_MonsterId", "String", JsonSerializer.SerializeToElement(new { type = "String", value = "monster-1" })));
        DapSourceVariableBinding[] bindings = [new("e_MonsterId", "monsterId", null, null)];

        var projected = DapSourceVariableProjector.Map(arguments, bindings, includeSynthetic: true);
        var monsterId = Assert.Single(projected.EnumerateArray());

        Assert.Equal("monsterId", monsterId.GetProperty("name").GetString());
        Assert.True(monsterId.GetProperty("assigned").GetBoolean());
        Assert.Equal("monster-1", monsterId.GetProperty("value").GetProperty("value").GetString());
    }

    [Fact]
    public void Completions_include_authored_roots_and_their_members()
    {
        var roots = DapCompletionBuilder.Build(Bindings, string.Empty, 1);
        var members = DapCompletionBuilder.Build(Bindings, "e.", 3);

        Assert.Equal(["ctx", "e"], roots.Select(Label));
        Assert.Equal(["Distance", "MonsterId"], members.Select(Label));
    }

    private static string? Label(object completion)
        => JsonSerializer.SerializeToElement(completion).GetProperty("label").GetString();

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
