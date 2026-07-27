using System.Reflection;
using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Timeout;

public sealed class BindingWallTimeSourceLifetimeTests
{
    private static readonly FieldInfo SharedSourceField =
        typeof(SandboxContext).GetField("_sharedWallTimeToken", BindingFlags.Instance | BindingFlags.NonPublic) ??
        throw new MissingFieldException(nameof(SandboxContext), "_sharedWallTimeToken");

    [Theory]
    [MemberData(nameof(CompletionCases))]
    public async Task Execution_releases_shared_deadline_source(
        ExecutionMode mode,
        BindingCompletion completion)
    {
        var source = await ExecuteAndCaptureSourceAsync(mode, completion);

        CollectUntilReleased(source);

        Assert.False(source.IsAlive);
    }

    public static TheoryData<ExecutionMode, BindingCompletion> CompletionCases()
        => new()
        {
            { ExecutionMode.Interpreted, BindingCompletion.Success },
            { ExecutionMode.Compiled, BindingCompletion.Success },
            { ExecutionMode.Interpreted, BindingCompletion.Failure },
            { ExecutionMode.Compiled, BindingCompletion.Failure },
            { ExecutionMode.Interpreted, BindingCompletion.Cancellation },
            { ExecutionMode.Compiled, BindingCompletion.Cancellation },
            { ExecutionMode.Interpreted, BindingCompletion.Timeout },
            { ExecutionMode.Compiled, BindingCompletion.Timeout }
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> ExecuteAndCaptureSourceAsync(
        ExecutionMode mode,
        BindingCompletion completion)
    {
        var binding = new CapturingBinding(completion);
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var wallTime = completion == BindingCompletion.Timeout
            ? TimeSpan.FromMilliseconds(25)
            : TimeSpan.FromHours(1);
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithFuel(1_000)
            .WithWallTime(wallTime)
            .WithMaxHostCalls(2)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        using var runCancellation = new CancellationTokenSource();
        var runToken = completion == BindingCompletion.Cancellation
            ? runCancellation.Token
            : CancellationToken.None;
        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false },
            runToken).AsTask();

        try
        {
            await binding.Invoked.WaitAsync(TimeSpan.FromSeconds(5));
            if (completion == BindingCompletion.Cancellation)
            {
                await runCancellation.CancelAsync();
            }

            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ExpectedError(completion), result.Error?.Code);
            Assert.Equal(completion == BindingCompletion.Success, result.Succeeded);
            return binding.Source ?? throw new InvalidOperationException("Binding did not capture its deadline source.");
        }
        finally
        {
            binding.Complete();
            _ = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    private static SandboxErrorCode? ExpectedError(BindingCompletion completion)
        => completion switch
        {
            BindingCompletion.Success => null,
            BindingCompletion.Failure => SandboxErrorCode.BindingFailure,
            BindingCompletion.Cancellation => SandboxErrorCode.Cancelled,
            BindingCompletion.Timeout => SandboxErrorCode.Timeout,
            _ => throw new ArgumentOutOfRangeException(nameof(completion))
        };

    private static void CollectUntilReleased(WeakReference source)
    {
        for (var i = 0; i < 5 && source.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Yield();
        }
    }

    private static string ModuleJson()
        => """
        {
          "id": "binding-wall-time-source-lifetime",
          "version": "1.0.0",
          "functions": [{
            "id": "main",
            "visibility": "entrypoint",
            "parameters": [],
            "returnType": "Unit",
            "body": [{ "op": "return", "value": { "call": "test.capture", "args": [] } }]
          }]
        }
        """;

    public enum BindingCompletion
    {
        Success,
        Failure,
        Cancellation,
        Timeout
    }

    private sealed class CapturingBinding(BindingCompletion completion)
    {
        private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<SandboxValue> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Invoked => _invoked.Task;

        public WeakReference? Source { get; private set; }

        public void Complete() => _pending.TrySetResult(SandboxValue.Unit);

        public BindingDescriptor Descriptor()
            => new(
                "test.capture",
                SemVersion.One,
                [],
                SandboxType.Unit,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(
                    typeof(CompiledRuntime).FullName!,
                    nameof(CompiledRuntime.CallBinding)))
            {
                IsAsync = completion is BindingCompletion.Cancellation or BindingCompletion.Timeout
            };

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            Source = new WeakReference(
                SharedSourceField.GetValue(context) ??
                throw new InvalidOperationException("Binding deadline source was not published before invocation."));
            _invoked.TrySetResult();
            return completion switch
            {
                BindingCompletion.Success => ValueTask.FromResult(SandboxValue.Unit),
                BindingCompletion.Failure => throw new InvalidOperationException("expected binding failure"),
                _ => new ValueTask<SandboxValue>(_pending.Task)
            };
        }
    }
}
