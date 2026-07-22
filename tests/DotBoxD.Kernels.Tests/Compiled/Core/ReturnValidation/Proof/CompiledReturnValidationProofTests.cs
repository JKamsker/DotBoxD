using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Tests.Compiled.Core.ReturnValidation.Proof;

public sealed class CompiledReturnValidationProofTests
{
    [Fact]
    public void Proof_requires_the_recording_context()
    {
        var owner = CreateContext();
        var other = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        owner.RecordCompiledReturnValidation(value, expectedType);

        Assert.False(other.TryConsumeCompiledReturnValidation(value, expectedType));
        Assert.True(owner.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    [Fact]
    public void Proof_requires_the_exact_value_reference_and_a_mismatch_consumes_it()
    {
        var context = CreateContext();
        var recorded = List(SandboxValue.FromInt32(1));
        var equalButDistinct = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        Assert.Equal(recorded, equalButDistinct);
        Assert.NotSame(recorded, equalButDistinct);
        context.RecordCompiledReturnValidation(recorded, expectedType);

        Assert.False(context.TryConsumeCompiledReturnValidation(equalButDistinct, expectedType));
        Assert.False(context.TryConsumeCompiledReturnValidation(recorded, expectedType));
    }

    [Fact]
    public void Proof_accepts_a_semantically_equal_expected_type()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var recordedType = SandboxType.List(SandboxType.I32);
        var equivalentType = SandboxType.List(SandboxType.I32);
        Assert.Equal(recordedType, equivalentType);
        Assert.NotSame(recordedType, equivalentType);
        context.RecordCompiledReturnValidation(value, recordedType);

        Assert.True(context.TryConsumeCompiledReturnValidation(value, equivalentType));
    }

    [Fact]
    public void Expected_type_mismatch_consumes_the_proof()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        context.RecordCompiledReturnValidation(value, expectedType);

        Assert.False(
            context.TryConsumeCompiledReturnValidation(
                value,
                SandboxType.List(SandboxType.I64)));
        Assert.False(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    [Fact]
    public void Successful_proof_is_single_use()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        context.RecordCompiledReturnValidation(value, expectedType);

        Assert.True(context.TryConsumeCompiledReturnValidation(value, expectedType));
        Assert.False(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    [Fact]
    public void Runtime_helper_returns_and_records_the_validated_value()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);

        var returned = CompiledRuntime.RequireValueTypeAndRecordValidation(
            context,
            value,
            expectedType);

        Assert.Same(value, returned);
        Assert.True(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    [Fact]
    public void Runtime_helper_rejects_deeply_malformed_value_before_publishing_proof()
    {
        var context = CreateContext();
        var malformedInner = new ListValue(
            [SandboxValue.FromString("wrong")],
            SandboxType.I32);
        var malformedOuter = new ListValue(
            [malformedInner],
            SandboxType.List(SandboxType.I32));
        var expectedType = SandboxType.List(SandboxType.List(SandboxType.I32));

        var exception = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.RequireValueTypeAndRecordValidation(
                context,
                malformedOuter,
                expectedType));

        Assert.Equal(SandboxErrorCode.InvalidInput, exception.Error.Code);
        Assert.Equal("function return type mismatch", exception.Error.SafeMessage);
        Assert.False(
            context.TryConsumeCompiledReturnValidation(
                malformedOuter,
                expectedType));
    }

    [Fact]
    public void Recursive_entrypoint_return_does_not_replace_the_outer_proof()
    {
        var context = CreateContext();
        var outer = List(SandboxValue.FromInt32(1));
        var recursive = List(SandboxValue.FromInt32(2));
        var expectedType = SandboxType.List(SandboxType.I32);
        context.EnterCall();
        context.RecordCompiledReturnValidation(outer, expectedType);
        context.EnterCall();

        try
        {
            _ = CompiledRuntime.RequireValueTypeAndRecordValidation(
                context,
                recursive,
                expectedType);
        }
        finally
        {
            context.ExitCall();
            context.ExitCall();
        }

        Assert.True(context.TryConsumeCompiledReturnValidation(outer, expectedType));
        Assert.False(context.TryConsumeCompiledReturnValidation(recursive, expectedType));
    }

    [Fact]
    public void Proof_does_not_cross_threads()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        using var proofRecorded = new ManualResetEventSlim();
        using var releaseOwner = new ManualResetEventSlim();
        Exception? ownerFailure = null;
        var ownerConsumed = false;
        var ownerThread = new Thread(() =>
        {
            try
            {
                context.RecordCompiledReturnValidation(value, expectedType);
            }
            catch (Exception exception)
            {
                ownerFailure = exception;
            }
            finally
            {
                proofRecorded.Set();
            }

            releaseOwner.Wait();
            if (ownerFailure is null)
            {
                try
                {
                    ownerConsumed = context.TryConsumeCompiledReturnValidation(value, expectedType);
                }
                catch (Exception exception)
                {
                    ownerFailure = exception;
                }
            }
        })
        {
            IsBackground = true
        };
        ownerThread.Start();

        try
        {
            Assert.True(proofRecorded.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(context.TryConsumeCompiledReturnValidation(value, expectedType));
        }
        finally
        {
            releaseOwner.Set();
            Assert.True(ownerThread.Join(TimeSpan.FromSeconds(5)));
        }

        Assert.Null(ownerFailure);
        Assert.True(ownerConsumed);
    }

    [Fact]
    public void Execution_cleanup_clears_the_proof()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        context.RecordCompiledReturnValidation(value, expectedType);

        context.ClearCompiledReturnValidation();

        Assert.False(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    [Fact]
    public void Reusable_context_reset_clears_the_proof()
    {
        var context = CreateContext();
        var value = List(SandboxValue.FromInt32(1));
        var expectedType = SandboxType.List(SandboxType.I32);
        context.RecordCompiledReturnValidation(value, expectedType);

        context.ResetForCompiledNoAuditReuse();

        Assert.False(context.TryConsumeCompiledReturnValidation(value, expectedType));
    }

    private static ListValue List(SandboxValue value)
        => new([value], SandboxType.I32);

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
}
