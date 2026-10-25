using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Timeout;

public sealed class BindingWallTimeCallbackCancellationSurpriseTests
{
    [Theory]
    [InlineData(ExecutionMode.Interpreted)]
    [InlineData(ExecutionMode.Compiled)]
    public async Task Caller_cancellation_does_not_leak_binding_wall_time_callback_failures(
        ExecutionMode mode)
    {
        var binding = new ThrowingWallTimeCallbackBinding();
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
            .WithWallTime(TimeSpan.FromSeconds(30))
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

            runCancellation.Cancel();

            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(result.Succeeded);
            Assert.Equal(SandboxErrorCode.Cancelled, result.Error!.Code);
            Assert.Equal(mode, result.ActualMode);
            Assert.Equal(1, binding.CallbackCount);
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
          "id": "binding-wall-time-callback-cancellation",
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

    private sealed class ThrowingWallTimeCallbackBinding
    {
        private readonly TaskCompletionSource _invoked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<SandboxValue> _pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callbackCount;

        public Task Invoked => _invoked.Task;

        public int CallbackCount => Volatile.Read(ref _callbackCount);

        public void Complete() => _pending.TrySetResult(SandboxValue.FromInt32(7));

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
            cancellationToken.Register(() =>
            {
                Interlocked.Increment(ref _callbackCount);
                throw new InvalidOperationException("expected callback failure");
            });
            _invoked.TrySetResult();
            return new ValueTask<SandboxValue>(_pending.Task);
        }
    }
}
