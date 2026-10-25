using System.Diagnostics;
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
        => await RunCancellationAsync(mode, deadline: false);

    [Fact]
    public async Task Wall_deadline_does_not_allow_a_throwing_binding_callback_to_terminate_the_process()
    {
        const string childVariable = "DOTBOXD_WALL_CALLBACK_CHILD";
        if (Environment.GetEnvironmentVariable(childVariable) == "1")
        {
            await RunCancellationAsync(ExecutionMode.Interpreted, deadline: true);
            await RunCancellationAsync(ExecutionMode.Compiled, deadline: true);
            return;
        }

        var start = new ProcessStartInfo("dotnet")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(typeof(BindingWallTimeCallbackCancellationSurpriseTests).Assembly.Location);
        start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~Wall_deadline_does_not_allow_a_throwing_binding_callback");
        start.Environment[childVariable] = "1";
        using var child = Process.Start(start)!;
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); }
        finally
        { if (!child.HasExited) { child.Kill(entireProcessTree: true); } }
        Assert.True(child.ExitCode == 0, await output + await error);
    }

    private static async Task RunCancellationAsync(ExecutionMode mode, bool deadline)
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
            .WithWallTime(deadline ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromSeconds(30))
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

            if (!deadline)
            { runCancellation.Cancel(); }

            var result = await execution.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(result.Succeeded);
            Assert.Equal(deadline ? SandboxErrorCode.Timeout : SandboxErrorCode.Cancelled, result.Error!.Code);
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
