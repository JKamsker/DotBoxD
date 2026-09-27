using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Compiled.Core;

internal static class CompiledAwaitPumpScopeFixture
{
    public static SandboxContext Context()
    {
        var policy = SandboxPolicyBuilder.Create().AllowRuntimeAsync()
            .WithFuel(1_000).WithWallTime(TimeSpan.FromSeconds(10)).Build();
        return new SandboxContext(
            SandboxRunId.New(), policy, new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]), new InMemoryAuditSink(), CancellationToken.None);
    }

    public static SandboxValue Dispatch(SandboxContext context, CancellationToken token = default)
    {
        var completion = new TaskCompletionSource<SandboxValue>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            return CompiledBindingDispatcher.AwaitBinding(context, new(completion.Task), token);
        }
        finally
        {
            completion.SetResult(SandboxValue.Unit);
        }
    }

    public static void AssertNoPump(SandboxContext context)
    {
        var error = Assert.Throws<SandboxRuntimeException>(() => Dispatch(context));
        Assert.Equal(SandboxErrorCode.BindingFailure, error.Error.Code);
        Assert.Equal("async pump is not installed", error.Error.SafeMessage);
    }

    internal sealed class Pump(int value = 42) : ICompiledAwaitPump, IDisposable
    {
        public SandboxValue Value { get; } = SandboxValue.FromInt32(value);
        public int Calls { get; private set; }
        public int DisposeCalls { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public SandboxValue RunToCompletion(ValueTask<SandboxValue> pending, CancellationToken cancellationToken)
        {
            Assert.False(pending.IsCompleted);
            Calls++;
            LastToken = cancellationToken;
            return Value;
        }

        public void Dispose() => DisposeCalls++;
    }
}
