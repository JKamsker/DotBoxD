using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests._TestSupport;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.ReviewFindings;

/// <summary>
/// Deep (validated) expression nesting multiplied by a host-raised call depth must surface as a
/// sandbox quota error instead of overflowing the managed stack, which would kill the process.
/// The run happens on a dedicated thread with a deliberately small stack so the guard, not the
/// stack, is what stops it.
/// </summary>
public sealed class ManagedStackGuardTests
{
    private const int StackBytes = 512 * 1024;
    private const int PrepareStackBytes = 256 * 1024 * 1024;
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Interpreted, true)]
    public void Deep_call_chain_through_nested_expressions_fails_with_quota_error(
        ExecutionMode mode,
        bool debugTrace)
    {
        using var host = SandboxTestHost.Create(compiler: true);
        // Validation (and the compiler's reachable-function walk) recurse along the call chain too and
        // are not covered by this guard, so prepare on a large stack and only execute on the small one.
        var plan = RunOnThread(PrepareStackBytes, () => host
            .PrepareAsync(
                Module(expressionDepth: 120, chainLength: 3_000),
                SandboxPolicyBuilder.Create()
                    .WithFuel(1_000_000_000)
                    .WithMaxCallDepth(100_000)
                    .Build())
            .AsTask()
            .GetAwaiter()
            .GetResult());
        var options = new SandboxExecutionOptions
        {
            Mode = mode,
            AllowFallbackToInterpreter = false,
            EnableDebugTrace = debugTrace
        };

        var result = RunOnThread(StackBytes, () => host
            .ExecuteAsync(plan, "main", SandboxValue.FromInt32(1), options)
            .AsTask()
            .GetAwaiter()
            .GetResult());

        Assert.False(result.Succeeded);
        Assert.Equal(SandboxErrorCode.QuotaExceeded, result.Error?.Code);
        Assert.Equal(mode, result.ActualMode);
    }

    private static T RunOnThread<T>(int stackBytes, Func<T> run)
    {
        T? result = default;
        Exception? failure = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = run();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            },
            stackBytes);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw new InvalidOperationException("sandbox run threw instead of returning a result", failure);
        }

        return result!;
    }

    // Recursion is rejected by validation, so build an acyclic chain f0 -> f1 -> ... where each
    // f_i(n) = 1 + (1 + (... + f_{i+1}(n))) calls the next function at its deepest nesting level.
    private static SandboxModule Module(int expressionDepth, int chainLength)
    {
        var n = new VariableExpression("n", Span);
        var functions = new List<SandboxFunction>(chainLength + 1);
        for (var i = 0; i < chainLength; i++)
        {
            Expression value = i == chainLength - 1
                ? n
                : new CallExpression($"f{i + 1}", [n], null, Span);
            for (var depth = 0; depth < expressionDepth; depth++)
            {
                value = new BinaryExpression(Literal(1), "+", value, Span);
            }

            functions.Add(new SandboxFunction(
                $"f{i}",
                IsEntrypoint: false,
                [new Parameter("n", SandboxType.I32)],
                SandboxType.I32,
                [new ReturnStatement(value, Span)]));
        }

        functions.Add(new SandboxFunction(
            "main",
            IsEntrypoint: true,
            [new Parameter("n", SandboxType.I32)],
            SandboxType.I32,
            [new ReturnStatement(new CallExpression("f0", [n], null, Span), Span)]));
        return new SandboxModule(
            "deep-call-chain",
            SemVersion.One,
            SemVersion.One,
            [],
            functions,
            new Dictionary<string, string>());
    }

    private static LiteralExpression Literal(int value) => new(SandboxValue.FromInt32(value), Span);
}
