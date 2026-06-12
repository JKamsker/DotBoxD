using SafeIR;

namespace SafeIR.Tests;

public sealed class SafeLoggingTests
{
    [Fact]
    public async Task Log_info_writes_sanitized_audit_event()
    {
        var result = await ExecuteLogAsync(
            """{ "call": "log.info", "args": [{ "string": "first\nsecond\tok" }] }""",
            SandboxPolicyBuilder.Create().GrantLogging().Build());

        Assert.True(result.Succeeded);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog");
        Assert.Equal("log.info", audit.BindingId);
        Assert.Equal("log.write", audit.CapabilityId);
        Assert.Equal("log:info", audit.ResourceId);
        Assert.Equal("first second ok", audit.Message);
        Assert.Equal(1, result.ResourceUsage.LogEvents);
    }

    [Fact]
    public async Task Log_warn_writes_warning_audit_event()
    {
        var result = await ExecuteLogAsync(
            """{ "call": "log.warn", "args": [{ "string": "careful" }] }""",
            SandboxPolicyBuilder.Create().GrantLogging().Build());

        Assert.True(result.Succeeded);
        Assert.Contains(result.AuditEvents, e =>
            e.Kind == "SandboxLog" &&
            e.BindingId == "log.warn" &&
            e.ResourceId == "log:warn" &&
            e.Message == "careful");
    }

    [Fact]
    public async Task Log_message_redacts_secret_shaped_values()
    {
        var result = await ExecuteLogAsync(
            """{ "call": "log.info", "args": [{ "string": "token=abc123 password: hunter2 ok" }] }""",
            SandboxPolicyBuilder.Create().GrantLogging().Build());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        var audit = Assert.Single(result.AuditEvents, e => e.Kind == "SandboxLog");
        Assert.DoesNotContain("abc123", audit.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", audit.Message, StringComparison.Ordinal);
        Assert.Equal("token=[redacted] password: [redacted] ok", audit.Message);
    }

    [Fact]
    public async Task Log_binding_requires_policy_grant()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(LogJson(
            """{ "call": "log.info", "args": [{ "string": "denied" }] }"""));

        var ex = await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, SandboxPolicyBuilder.Create().Build()));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-POLICY-CAP");
    }

    [Fact]
    public async Task Log_event_limit_is_enforced()
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync("""
        {
          "id": "log-test",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "expr", "value": { "call": "log.info", "args": [{ "string": "one" }] } },
                { "op": "return", "value": { "call": "log.info", "args": [{ "string": "two" }] } }
              ]
            }
          ]
        }
        """);
        var policy = SandboxPolicyBuilder.Create().GrantLogging().WithMaxLogEvents(1).Build();
        var plan = await host.PrepareAsync(module, policy);

        var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
    }

    [Fact]
    public async Task Log_message_length_limit_is_enforced()
    {
        var result = await ExecuteLogAsync(
            """{ "call": "log.info", "args": [{ "string": "too long" }] }""",
            SandboxPolicyBuilder.Create().GrantLogging().WithMaxLogMessageLength(4).Build());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error!.Code);
        Assert.DoesNotContain(result.AuditEvents, e => e.Kind == "SandboxLog");
    }

    [Fact]
    public void Log_limits_are_part_of_policy_hash()
    {
        var first = SandboxPolicyBuilder.Create().GrantLogging().WithMaxLogEvents(1).Build();
        var second = SandboxPolicyBuilder.Create().GrantLogging().WithMaxLogEvents(2).Build();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    private static async Task<SandboxExecutionResult> ExecuteLogAsync(string expression, SandboxPolicy policy)
    {
        var host = SandboxTestHost.Create();
        var module = await host.ParseJsonAsync(LogJson(expression));
        var plan = await host.PrepareAsync(module, policy);
        return await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
    }

    private static string LogJson(string expression)
        => $$"""
        {
          "id": "log-test",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "log.write", "reason": "test logs" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                { "op": "return", "value": {{expression}} }
              ]
            }
          ]
        }
        """;
}
