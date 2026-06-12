using SafeIR.Plugins;

namespace SafeIR.Tests;

public sealed class PublicModelImmutabilityTests
{
    private static readonly SourceSpan Span = new(1, 1);
    private static readonly Expression Literal = new LiteralExpression(SandboxValue.FromInt32(1), Span);

    [Fact]
    public void Sandbox_module_and_function_copy_collection_inputs()
    {
        var parameters = new List<Parameter> { new("value", SandboxType.I32) };
        var body = new List<Statement> { new ReturnStatement(Literal, Span) };
        var function = new SandboxFunction("main", true, parameters, SandboxType.I32, body);
        parameters.Add(new Parameter("other", SandboxType.I32));
        body.Clear();

        var capabilities = new List<CapabilityRequest> { new("log.write", null) };
        var functions = new List<SandboxFunction> { function };
        var metadata = new Dictionary<string, string> { ["pluginId"] = "safe" };
        var module = new SandboxModule("module", SemVersion.One, SemVersion.One, capabilities, functions, metadata);
        capabilities.Clear();
        functions.Clear();
        metadata["pluginId"] = "mutated";

        Assert.Single(function.Parameters);
        Assert.Single(function.Body);
        Assert.Single(module.CapabilityRequests);
        Assert.Single(module.Functions);
        Assert.Equal("safe", module.Metadata["pluginId"]);
    }

    [Fact]
    public void Statement_and_expression_nodes_copy_collection_inputs()
    {
        var statements = new List<Statement> { new ReturnStatement(Literal, Span) };
        var ifStatement = new IfStatement(Literal, statements, statements, Span);
        var whileStatement = new WhileStatement(Literal, statements, Span);
        var rangeStatement = new ForRangeStatement("i", Literal, Literal, statements, Span);
        var arguments = new List<Expression> { Literal };
        var call = new CallExpression("test", arguments, null, Span);
        statements.Clear();
        arguments.Clear();

        Assert.Single(ifStatement.Then);
        Assert.Single(ifStatement.Else);
        Assert.Single(whileStatement.Body);
        Assert.Single(rangeStatement.Body);
        Assert.Single(call.Arguments);
    }

    [Fact]
    public void With_updates_copy_collection_inputs()
    {
        var module = EmptyModule();
        var functions = new List<SandboxFunction> { EmptyFunction() };
        var updated = module with { Functions = functions };
        functions.Clear();

        Assert.Single(updated.Functions);
    }

    [Fact]
    public void Plugin_manifest_copies_collection_inputs()
    {
        var effects = new List<string> { "Cpu" };
        var settings = new List<LiveSettingDefinition> { new("Enabled", "bool", true) };
        var subscriptions = new List<HookSubscriptionManifest> { new("DamageEvent", "Kernel") };
        var manifest = new PluginManifest("plugin", "contract", ExecutionMode.Interpreted, effects, settings, subscriptions);
        effects.Clear();
        settings.Clear();
        subscriptions.Clear();

        Assert.Single(manifest.Effects);
        Assert.Single(manifest.LiveSettings);
        Assert.Single(manifest.Subscriptions);
    }

    [Fact]
    public void Sandbox_values_copy_collection_inputs()
    {
        var listBacking = new List<SandboxValue> { SandboxValue.FromInt32(1) };
        var list = (ListValue)SandboxValue.FromList(listBacking);
        listBacking.Add(SandboxValue.FromInt32(2));

        var mapBacking = new Dictionary<SandboxValue, SandboxValue>
        {
            [SandboxValue.FromString("first")] = SandboxValue.FromInt32(1)
        };
        var map = (MapValue)SandboxValue.FromMap(mapBacking, SandboxType.String, SandboxType.I32);
        mapBacking[SandboxValue.FromString("second")] = SandboxValue.FromInt32(2);

        Assert.Single(list.Values);
        Assert.Single(map.Values);
    }

    [Fact]
    public void Validation_exception_copies_diagnostic_inputs()
    {
        var diagnostics = new List<SandboxDiagnostic>
        {
            new("E-ONE", "first")
        };
        var exception = new SandboxValidationException(diagnostics);
        diagnostics.Add(new SandboxDiagnostic("E-TWO", "second"));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal("E-ONE", diagnostic.Code);
    }

    [Fact]
    public void Audit_event_and_sink_copy_field_inputs()
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["resourceKind"] = "network"
        };
        var auditEvent = new SandboxAuditEvent(
            SandboxRunId.New(),
            "BindingCall",
            DateTimeOffset.UtcNow,
            true,
            Fields: fields);
        fields["resourceKind"] = "mutated-before-write";

        var sink = new InMemoryAuditSink();
        sink.Write(auditEvent);
        fields["resourceKind"] = "mutated-after-write";

        Assert.Equal("network", auditEvent.Fields!["resourceKind"]);
        Assert.Equal("network", sink.Events[0].Fields!["resourceKind"]);
    }

    [Fact]
    public void Execution_result_copies_audit_event_list_inputs()
    {
        var events = new List<SandboxAuditEvent>
        {
            new(SandboxRunId.New(), "RunSummary", DateTimeOffset.UtcNow, true)
        };
        var result = new SandboxExecutionResult
        {
            Succeeded = true,
            Value = SandboxValue.Unit,
            ResourceUsage = new ResourceMeter(new ResourceLimits(MaxFuel: 1_000)).Snapshot(),
            AuditEvents = events,
            ActualMode = ExecutionMode.Interpreted,
            ExecutionDispatched = true,
            ModuleHash = "module",
            PlanHash = "plan",
            PolicyHash = "policy"
        };
        events.Clear();

        Assert.Single(result.AuditEvents);
    }

    private static SandboxModule EmptyModule()
        => new("module", SemVersion.One, SemVersion.One, [], [EmptyFunction()], new Dictionary<string, string>());

    private static SandboxFunction EmptyFunction()
        => new("main", true, [], SandboxType.I32, [new ReturnStatement(Literal, Span)]);
}
