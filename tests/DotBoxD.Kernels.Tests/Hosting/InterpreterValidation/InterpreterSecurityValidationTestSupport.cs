using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Hosting;

internal static class InterpreterSecurityValidationTestSupport
{
    public const string AuditMarker = "untrusted interpreter security evidence";

    public static SandboxPolicy Policy(bool deterministic = false)
    {
        var builder = SandboxPolicyBuilder.Create()
            .WithFuel(1_000)
            .WithMaxHostCalls(10);
        if (deterministic)
        {
            builder.Deterministic(DateTimeOffset.UnixEpoch.AddDays(1), 7);
        }

        return builder.Build();
    }

    public static async ValueTask<InterpreterValidationOutcome> ExecuteAsync(
        string moduleJson,
        SandboxPolicy policy,
        ISandboxInterpreter? interpreter = null,
        Action<SandboxHostBuilder>? configure = null,
        SandboxExecutionOptions? options = null)
    {
        var observed = new List<SandboxAuditEvent>();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            configure?.Invoke(builder);
            builder.UseInterpreter(interpreter);
            builder.ForwardAuditEventsTo(observed.Add);
        });
        var module = await host.ImportJsonAsync(moduleJson);
        var plan = await host.PrepareAsync(module, policy);
        var result = await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            options ?? new SandboxExecutionOptions { Mode = ExecutionMode.Interpreted });
        return new InterpreterValidationOutcome(plan, result, observed);
    }

    public static void AssertRejectedWithoutPublication(
        InterpreterValidationOutcome outcome,
        string marker = AuditMarker)
    {
        Assert.False(outcome.Result.Succeeded);
        Assert.Equal(SandboxErrorCode.HostFailure, outcome.Result.Error!.Code);
        Assert.Equal("interpreter execution failed", outcome.Result.Error.SafeMessage);
        Assert.Equal(outcome.Result.AuditEvents, outcome.Observed);
        Assert.DoesNotContain(outcome.Observed, auditEvent =>
            auditEvent.Message == marker ||
            auditEvent.CapabilityId == marker ||
            auditEvent.Fields?.Values.Contains(marker, StringComparer.Ordinal) == true);
    }

    public static SandboxExecutionResult ReplaceFirstAudit(
        SandboxExecutionResult result,
        Func<SandboxAuditEvent, bool> predicate,
        Func<SandboxAuditEvent, SandboxAuditEvent> replace)
    {
        var replaced = false;
        var events = result.AuditEvents.Select(auditEvent =>
        {
            if (replaced || !predicate(auditEvent))
            {
                return auditEvent;
            }

            replaced = true;
            return replace(auditEvent);
        }).ToArray();
        Assert.True(replaced, "Expected the interpreter to emit matching audit evidence.");
        return result with { AuditEvents = events };
    }

    public static IReadOnlyDictionary<string, string> WithField(
        IReadOnlyDictionary<string, string> fields,
        string name,
        string value)
        => new Dictionary<string, string>(fields, StringComparer.Ordinal)
        {
            [name] = value
        };

    public static BindingDescriptor AuditedBinding(
        string id,
        AuditLevel auditLevel = AuditLevel.PerCall,
        int? maxCallsPerRun = null,
        string durationMs = "0",
        string? reportedCapability = null,
        SandboxType? returnType = null,
        SandboxValue? returnValue = null)
        => new(
            id,
            SemVersion.One,
            [],
            returnType ?? SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            new BindingCostModel(BaseFuel: 1, MaxCallsPerRun: maxCallsPerRun),
            auditLevel,
            BindingSafety.PureHostFacade,
            (context, _, _) =>
            {
                var timestamp = context.AuditTimestamp();
                context.Audit.Write(new SandboxAuditEvent(
                    context.RunId,
                    BindingAuditKinds.BindingCall,
                    timestamp,
                    true,
                    BindingId: id,
                    CapabilityId: reportedCapability,
                    Effect: SandboxEffect.Cpu,
                    ResourceId: $"test:{id}",
                    Message: AuditMarker,
                    Fields: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["resourceKind"] = "test",
                        ["durationMs"] = durationMs,
                        ["moduleHash"] = context.ModuleHash,
                        ["policyHash"] = context.PolicyHash
                    }));
                return ValueTask.FromResult(returnValue ?? SandboxValue.Unit);
            },
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    public static string SingleBindingModule(string bindingId, string returnType = "Unit")
        => $$"""
        {
          "id": "interpreter-security-single-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "{{returnType}}",
              "body": [
                { "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }
              ]
            }
          ]
        }
        """;

    public static string DoubleBindingModule(string bindingId)
        => $$"""
        {
          "id": "interpreter-security-double-binding",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "{{bindingId}}", "args": [] } },
                { "op": "return", "value": { "call": "{{bindingId}}", "args": [] } }
              ]
            }
          ]
        }
        """;

    public static string MathModuleWithUnrelatedHelper()
        => """
        {
          "id": "interpreter-security-debug-trace",
          "version": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "math.abs", "args": [{ "i32": -1 }] } }
              ]
            },
            {
              "id": "helper",
              "visibility": "private",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "i32": 0 } }]
            }
          ]
        }
        """;

    public static SandboxAuditEvent[] Resequence(IEnumerable<SandboxAuditEvent> events)
        => events.Select((auditEvent, index) => auditEvent with { SequenceNumber = index + 1 }).ToArray();
}

internal sealed record InterpreterValidationOutcome(
    ExecutionPlan Plan,
    SandboxExecutionResult Result,
    IReadOnlyList<SandboxAuditEvent> Observed);

internal sealed class TransformingInterpreter(
    Func<ExecutionPlan, SandboxExecutionResult, SandboxExecutionResult> transform) : ISandboxInterpreter
{
    private readonly SandboxInterpreter _inner = new();

    public async ValueTask<SandboxExecutionResult> ExecuteAsync(
        ExecutionPlan plan,
        string entrypoint,
        SandboxValue input,
        SandboxExecutionOptions options,
        CancellationToken cancellationToken)
    {
        var result = await _inner.ExecuteAsync(plan, entrypoint, input, options, cancellationToken)
            .ConfigureAwait(false);
        return transform(plan, result);
    }
}
