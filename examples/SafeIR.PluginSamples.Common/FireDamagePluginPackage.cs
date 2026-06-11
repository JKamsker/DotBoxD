namespace SafeIR.PluginSamples.Common;

using SafeIR;
using SafeIR.Plugins;

public static class FireDamagePluginPackage
{
    private static readonly SourceSpan Span = new(1, 1);

    public static PluginPackage Create()
    {
        var settings = new[] {
            new LiveSettingDefinition("Enabled", "bool", true),
            new LiveSettingDefinition("DamageType", "string", "fire"),
            new LiveSettingDefinition("MinDamage", "int", 100, 0, 10_000)
        };
        var manifest = new PluginManifest(
            "fire-damage",
            "IEventKernel<DamageEvent>",
            ExecutionMode.Auto,
            ["Cpu", "GameStateWrite", "Audit"],
            settings,
            [new HookSubscriptionManifest("DamageEvent", "FireDamageKernel")]);
        return PluginPackage.Create(manifest, CreateModule(settings));
    }

    private static SandboxModule CreateModule(IReadOnlyList<LiveSettingDefinition> settings)
        => new(
            "fire-damage",
            SemVersion.One,
            SemVersion.One,
            [new CapabilityRequest(PluginMessageBindings.CapabilityId, "send damage notifications")],
            [ShouldHandle(settings), Handle(settings)],
            new Dictionary<string, string> { ["pluginId"] = "fire-damage" });

    private static SandboxFunction ShouldHandle(IReadOnlyList<LiveSettingDefinition> settings)
        => new(
            "ShouldHandle",
            true,
            Parameters(settings),
            SandboxType.Bool,
            [new ReturnStatement(
                And(
                    Var("Enabled"),
                    And(
                        Eq(Var("eventDamageType"), Var("DamageType")),
                        Ge(Var("amount"), Var("MinDamage")))),
                Span)]);

    private static SandboxFunction Handle(IReadOnlyList<LiveSettingDefinition> settings)
        => new(
            "Handle",
            true,
            Parameters(settings),
            SandboxType.Unit,
            [new ReturnStatement(
                new CallExpression(
                    PluginMessageBindings.SendBindingId,
                    [Var("targetId"), Str("Ouch, fire.")],
                    null,
                    Span),
                Span)]);

    private static IReadOnlyList<Parameter> Parameters(IReadOnlyList<LiveSettingDefinition> settings)
        => DamageEventAdapter.Instance.Parameters
            .Concat(settings.Select(s => new Parameter(s.Name, TypeOf(s.Type))))
            .ToArray();

    private static SandboxType TypeOf(string type)
        => type switch {
            "bool" => SandboxType.Bool,
            "int" => SandboxType.I32,
            "string" => SandboxType.String,
            _ => throw new InvalidOperationException($"Unsupported sample setting type '{type}'.")
        };

    private static Expression Var(string name) => new VariableExpression(name, Span);
    private static Expression Str(string value) => new LiteralExpression(SandboxValue.FromString(value), Span);
    private static Expression Eq(Expression left, Expression right) => new BinaryExpression(left, "==", right, Span);
    private static Expression Ge(Expression left, Expression right) => new BinaryExpression(left, ">=", right, Span);
    private static Expression And(Expression left, Expression right) => new BinaryExpression(left, "&&", right, Span);
}
