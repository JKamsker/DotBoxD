using System.Runtime.CompilerServices;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ReturnValidation.Proof;

public sealed class CompiledReturnValidationLifetimeTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Abandoned_cross_thread_proof_does_not_retain_its_targets()
    {
        using var ready = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        ProofReferences? references = null;
        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                references = PublishAbandonedProof();
            }
            catch (Exception exception)
            {
                workerError = exception;
            }
            finally
            {
                ready.Set();
            }

            release.Wait(TestTimeout);
        })
        {
            IsBackground = true
        };
        worker.Start();

        try
        {
            Assert.True(ready.Wait(TestTimeout));
            Assert.Null(workerError);
            Assert.NotNull(references);
            Collect();

            Assert.False(references.Context.TryGetTarget(out _));
            Assert.False(references.Value.TryGetTarget(out _));
            Assert.False(references.Type.TryGetTarget(out _));
        }
        finally
        {
            release.Set();
            Assert.True(worker.Join(TestTimeout));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ProofReferences PublishAbandonedProof()
    {
        var context = CreateContext();
        var value = new ListValue([SandboxValue.FromInt32(1)], SandboxType.I32);
        var expectedType = SandboxType.List(SandboxType.I32);
        context.RecordCompiledReturnValidation(value, expectedType);
        return new ProofReferences(
            new WeakReference<SandboxContext>(context),
            new WeakReference<SandboxValue>(value),
            new WeakReference<SandboxType>(expectedType));
    }

    private static SandboxContext CreateContext()
    {
        var policy = SandboxPolicyBuilder.Create().Build();
        return new SandboxContext(
            SandboxRunId.New(),
            policy,
            new ResourceMeter(policy.ResourceLimits),
            new BindingRegistry([]),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed record ProofReferences(
        WeakReference<SandboxContext> Context,
        WeakReference<SandboxValue> Value,
        WeakReference<SandboxType> Type);
}
