using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using DotBoxD.Abstractions;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Hooks;

namespace DotBoxD.Plugins.Generated.Tests;

[Hook("debug.damage", typeof(DebugDamageResult))]
public sealed record DebugDamageContext(int Damage);

[HookResult]
public readonly partial record struct DebugDamageResult(bool Success, string? Reason, int Damage);

public sealed class DebuggableLocalTerminalTests
{
    [Fact]
    public async Task Remote_RunLocal_terminal_body_with_debugger_break_remains_native()
    {
        var received = new List<int>();
        string? stackDump = null;
        using var h = new RunLocalHarness<ChainAggroEvent>();

        h.Hooks.On<ChainAggroEvent>()
            .Where(e => e.Distance <= 4)
            .Select(e => e.Distance)
            .RunLocal((distance, ctx) =>
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                stackDump = CaptureAndAssertTerminalStack();
                received.Add(distance);
            });

        await h.PublishAsync(new ChainAggroEvent("m-1", 3));
        await h.PublishAsync(new ChainAggroEvent("m-2", 99));

        Assert.Equal(3, Assert.Single(received));
        Assert.NotNull(stackDump);
    }

    [Fact]
    public async Task Remote_RegisterLocal_terminal_body_with_debugger_break_remains_native()
    {
        string? stackDump = null;
        using var server = PluginServer.Create(defaultPolicy: TestPolicies.Chain());
        var localHandlers = new RemoteLocalHandlerRegistry();
        var registry = RemoteRegistry(server, localHandlers);

        registry.On<DebugDamageContext>()
            .Where(ctx => ctx.Damage > 10)
            .RegisterLocal((ctx, hookContext) =>
            {
                if (Debugger.IsAttached)
                {
                    Debugger.Break();
                }

                stackDump = CaptureAndAssertTerminalStack();
                return DebugDamageResult.Ok().WithDamage(ctx.Damage * 2);
            });

        var miss = await server.Hooks.FireAsync<DebugDamageContext, DebugDamageResult>(new DebugDamageContext(5));
        var hit = await server.Hooks.FireAsync<DebugDamageContext, DebugDamageResult>(new DebugDamageContext(12));

        Assert.Null(miss);
        Assert.Equal(24, hit!.Value.Damage);
        Assert.NotNull(stackDump);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string CaptureAndAssertTerminalStack()
    {
        var frames = new StackTrace(fNeedFileInfo: true).GetFrames() ?? [];
        var dump = RenderStack(frames);
        Assert.True(frames.Length > 1, dump);

        var caller = frames[1].GetMethod();
        Assert.True(IsAuthoredTestFrame(caller), dump);
        if (frames[1].GetFileName() is { Length: > 0 } callerFile)
        {
            Assert.True(callerFile.EndsWith(nameof(DebuggableLocalTerminalTests) + ".cs", StringComparison.Ordinal), dump);
        }

        Assert.DoesNotContain("HookChainInterceptors", dump, StringComparison.Ordinal);
        Assert.DoesNotContain(".HookChain_", dump, StringComparison.Ordinal);
        Assert.DoesNotContain("DotBoxDMergeableIrStepInterceptors", dump, StringComparison.Ordinal);
        Assert.False(frames.Any(IsGeneratedDotBoxDFrame), dump);
        return dump;
    }

    private static bool IsAuthoredTestFrame(MethodBase? method)
    {
        for (var declaring = method?.DeclaringType; declaring is not null; declaring = declaring.DeclaringType)
        {
            if (declaring == typeof(DebuggableLocalTerminalTests))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsGeneratedDotBoxDFrame(StackFrame frame)
    {
        var method = frame.GetMethod();
        var typeName = method?.DeclaringType?.FullName ?? string.Empty;
        var methodName = method?.Name ?? string.Empty;
        return methodName.StartsWith("Intercept_", StringComparison.Ordinal) ||
            typeName.Equals("DotBoxD.Plugins.Generated.HookChainInterceptors", StringComparison.Ordinal) ||
            typeName.Contains(".HookChain_", StringComparison.Ordinal) ||
            typeName.Contains("DotBoxDMergeableIrStepInterceptors", StringComparison.Ordinal);
    }

    private static string RenderStack(StackFrame[] frames)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < frames.Length; i++)
        {
            var method = frames[i].GetMethod();
            builder
                .Append(i)
                .Append(": ")
                .Append(method?.DeclaringType?.FullName ?? "<unknown>")
                .Append('.')
                .Append(method?.Name ?? "<unknown>");
            if (frames[i].GetFileName() is { Length: > 0 } fileName)
            {
                builder
                    .Append(" at ")
                    .Append(fileName)
                    .Append(':')
                    .Append(frames[i].GetFileLineNumber());
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static RemoteHookRegistry RemoteRegistry(
        PluginServer server,
        RemoteLocalHandlerRegistry localHandlers)
        => new(
            async package =>
            {
                var kernel = await server.InstallAsync(package).ConfigureAwait(false);
                var subscription = Assert.Single(package.Manifest.Subscriptions);
                var subscriptionId = kernel.CallbackSubscriptionId ?? kernel.Manifest.PluginId;
                server.Hooks.On<DebugDamageContext>().UseProjectingResult(
                    kernel,
                    subscriptionId,
                    typeof(DebugDamageResult),
                    (id, payload, token) => localHandlers.DispatchResultAsync(
                        id,
                        payload.ToArray(),
                        new HookContext(new InMemoryPluginMessageSink(), token),
                        token),
                    subscription.Priority);
                return subscriptionId;
            },
            localHandlers);
}
