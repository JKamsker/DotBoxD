using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Timeout;

public sealed class CustomAsyncBindingWallTimeTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted, false)]
    [InlineData(ExecutionMode.Interpreted, true)]
    [InlineData(ExecutionMode.Compiled, false)]
    [InlineData(ExecutionMode.Compiled, true)]
    public async Task Async_binding_that_ignores_wall_time_cancellation_returns_timeout(
        ExecutionMode mode,
        bool liveRunToken)
    {
        var binding = new PendingAsyncBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.FromMilliseconds(25))
            .WithMaxHostCalls(2)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        using var runCancellation = liveRunToken ? new CancellationTokenSource() : null;

        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false },
            runCancellation?.Token ?? CancellationToken.None).AsTask();

        try
        {
            await binding.Invoked.WaitAsync(TimeSpan.FromSeconds(5));

            var completed = await Task.WhenAny(
                execution,
                Task.Delay(TimeSpan.FromMilliseconds(750)));

            Assert.Same(execution, completed);
            Assert.False(binding.Pending.IsCompleted);

            var result = await execution;
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Timeout, result.Error!.Code);
            Assert.Equal(mode, result.ActualMode);
            Assert.Equal(1, binding.InvocationCount);
            Assert.Equal(1, result.ResourceUsage.HostCalls);
        }
        finally
        {
            binding.Complete();
            _ = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Pending_binding_with_live_run_token_returns_cancelled(ExecutionMode mode)
    {
        var binding = new PendingAsyncBinding();
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(binding.Descriptor());
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson());
        var policy = SandboxPolicyBuilder.Create()
            .AllowRuntimeAsync()
            .WithFuel(1_000)
            .WithWallTime(TimeSpan.FromSeconds(5))
            .WithMaxHostCalls(2)
            .Build();
        var plan = await host.PrepareAsync(module, policy);
        using var runCancellation = new CancellationTokenSource();
        var execution = host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false },
            runCancellation.Token).AsTask();

        try
        {
            await binding.Invoked.WaitAsync(TimeSpan.FromSeconds(5));
            await runCancellation.CancelAsync();
            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
            Assert.Equal(mode, result.ActualMode);
            Assert.True(binding.ObservedToken.IsCancellationRequested);
            Assert.Equal(1, result.ResourceUsage.HostCalls);
        }
        finally
        {
            binding.Complete();
            _ = await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(5)));
        }
    }

    private static string ModuleJson()
        => """
        {
          "id": "pending-async-binding-wall-time",
          "version": "1.0.0",
          "targetSandboxVersion": "1.0.0",
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                { "op": "return", "value": { "call": "test.pending", "args": [] } }
              ]
            }
          ]
        }
        """;

    private sealed class PendingAsyncBinding
    {
        private readonly TaskCompletionSource<SandboxValue> _invoked =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<SandboxValue> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _invocationCount;
        private CancellationToken _observedToken;

        public Task Invoked => _invoked.Task;

        public Task Pending => _pending.Task;

        public int InvocationCount => Volatile.Read(ref _invocationCount);

        public CancellationToken ObservedToken => _observedToken;

        public void Complete()
            => _pending.TrySetResult(SandboxValue.FromInt32(7));

        public BindingDescriptor Descriptor()
            => new(
                "test.pending",
                SemVersion.One,
                [],
                SandboxType.I32,
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
                IsAsync = true
            };

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            _observedToken = cancellationToken;
            Interlocked.Increment(ref _invocationCount);
            _invoked.TrySetResult(SandboxValue.Unit);
            return new ValueTask<SandboxValue>(_pending.Task);
        }
    }
}
