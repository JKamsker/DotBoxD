using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using SandboxHost = DotBoxD.Hosting.Execution.SandboxHost;

namespace DotBoxD.Kernels.Tests.Compiled.Regression.Performance.BindingDispatch;

public sealed class CompiledThreeArgumentBindingFastPathTests
{
    [Fact]
    public async Task Compiled_binding_uses_three_argument_invoker_without_argument_list()
    {
        var invoker = new FastAddBinding();
        var result = await ExecuteCompiledAsync(invoker.Descriptor());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public async Task Compiled_binding_falls_back_to_regular_invoker()
    {
        var invoker = new RegularAddBinding();
        var result = await ExecuteCompiledAsync(invoker.Descriptor());

        Assert.True(result.Succeeded, result.Error?.SafeMessage);
        Assert.Equal(ExecutionMode.Compiled, result.ActualMode);
        Assert.Equal(123, ((I32Value)result.Value!).Value);
        Assert.Equal(1, invoker.Calls);
    }

    [Fact]
    public void Scalar_dispatch_accepts_structural_arguments()
    {
        var invoker = new TrackingBinding();
        var descriptor = invoker.Descriptor(
            [
                SandboxType.List(SandboxType.I32),
                SandboxType.Record([SandboxType.Map(SandboxType.String, SandboxType.I32)]),
                SandboxType.I32
            ]);
        using var context = Context(descriptor);
        var list = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);
        var map = SandboxValue.FromMap(
            new Dictionary<SandboxValue, SandboxValue>
            {
                [SandboxValue.FromString("one")] = SandboxValue.FromInt32(1)
            },
            SandboxType.String,
            SandboxType.I32);
        var record = SandboxValue.FromRecord([map]);

        var result = CompiledRuntime.CallBinding3(
            context,
            descriptor.Id,
            list,
            record,
            SandboxValue.FromInt32(3));

        Assert.Same(SandboxValue.Unit, result);
        Assert.Equal(1, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public void Scalar_dispatch_rejects_structural_mismatch_before_invocation()
    {
        var invoker = new TrackingBinding();
        var descriptor = invoker.Descriptor(
            [SandboxType.List(SandboxType.I64), SandboxType.Unit, SandboxType.I32]);
        using var context = Context(descriptor);
        var wrong = SandboxValue.FromList([SandboxValue.FromInt32(1)], SandboxType.I32);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.CallBinding3(
                context,
                descriptor.Id,
                wrong,
                SandboxValue.Unit,
                SandboxValue.FromInt32(3)));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.Equal(0, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    [Fact]
    public void Scalar_dispatch_rejects_count_mismatch_before_invocation()
    {
        var invoker = new TrackingBinding();
        var descriptor = invoker.Descriptor([SandboxType.I32, SandboxType.I32]);
        using var context = Context(descriptor);

        var error = Assert.Throws<SandboxRuntimeException>(
            () => CompiledRuntime.CallBinding3(
                context,
                descriptor.Id,
                SandboxValue.FromInt32(1),
                SandboxValue.FromInt32(2),
                SandboxValue.FromInt32(3)));

        Assert.Equal(SandboxErrorCode.ValidationError, error.Error.Code);
        Assert.Equal(0, invoker.FastCalls);
        Assert.Equal(0, invoker.ListCalls);
    }

    private static async Task<SandboxExecutionResult> ExecuteCompiledAsync(BindingDescriptor descriptor)
    {
        using var host = SandboxHost.Create(builder =>
        {
            builder.AddBinding(descriptor);
            builder.UseInterpreter();
            builder.UseCompilerIfAvailable();
        });
        var module = await host.ImportJsonAsync(ModuleJson);
        var plan = await host.PrepareAsync(
            module,
            SandboxPolicyBuilder.Create().WithFuel(1_000).Build());
        return await host.ExecuteAsync(
            plan,
            "main",
            SandboxValue.Unit,
            new SandboxExecutionOptions
            {
                Mode = ExecutionMode.Compiled,
                AllowFallbackToInterpreter = false
            });
    }

    private static SandboxContext Context(params BindingDescriptor[] descriptors)
    {
        var limits = new ResourceLimits(MaxFuel: 1_000_000, MaxAllocatedBytes: 1_000_000);
        return new SandboxContext(
            SandboxRunId.New(),
            SandboxPolicyBuilder.Create().Build() with { ResourceLimits = limits },
            new ResourceMeter(limits),
            new BindingRegistryBuilder().AddRange(descriptors).Build(),
            new InMemoryAuditSink(),
            CancellationToken.None);
    }

    private const string ModuleJson = """
    {
      "id": "compiled-three-arg-binding-fast-path",
      "version": "1.0.0",
      "functions": [{
        "id": "main",
        "visibility": "entrypoint",
        "parameters": [],
        "returnType": "I32",
        "body": [{
          "op": "return",
          "value": {
            "call": "test.add3",
            "args": [{ "i32": 1 }, { "i32": 2 }, { "i32": 3 }]
          }
        }]
      }]
    }
    """;

    private sealed class FastAddBinding : IThreeArgumentBindingInvoker
    {
        public int FastCalls { get; private set; }
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor()
            => DescriptorFactory(Invoke);

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return Combine(args[0], args[1], args[2]);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            SandboxValue arg2,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return Combine(arg0, arg1, arg2);
        }
    }

    private sealed class RegularAddBinding
    {
        public int Calls { get; private set; }

        public BindingDescriptor Descriptor()
            => DescriptorFactory(Invoke);

        private ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Combine(args[0], args[1], args[2]);
        }
    }

    private sealed class TrackingBinding : IThreeArgumentBindingInvoker
    {
        public int FastCalls { get; private set; }
        public int ListCalls { get; private set; }

        public BindingDescriptor Descriptor(IReadOnlyList<SandboxType> parameters)
            => new(
                "test.track3",
                SemVersion.One,
                parameters,
                SandboxType.Unit,
                SandboxEffect.Cpu,
                null,
                BindingCostModel.Fixed(1),
                AuditLevel.None,
                BindingSafety.PureHostFacade,
                Invoke,
                CompiledBinding.RuntimeStub(
                    typeof(CompiledRuntime).FullName!,
                    nameof(CompiledRuntime.CallBinding)));

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            IReadOnlyList<SandboxValue> args,
            CancellationToken cancellationToken)
        {
            ListCalls++;
            return ValueTask.FromResult(SandboxValue.Unit);
        }

        public ValueTask<SandboxValue> Invoke(
            SandboxContext context,
            SandboxValue arg0,
            SandboxValue arg1,
            SandboxValue arg2,
            CancellationToken cancellationToken)
        {
            FastCalls++;
            return ValueTask.FromResult(SandboxValue.Unit);
        }
    }

    private static BindingDescriptor DescriptorFactory(BindingInvoker invoke)
        => new(
            "test.add3",
            SemVersion.One,
            [SandboxType.I32, SandboxType.I32, SandboxType.I32],
            SandboxType.I32,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            invoke,
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static ValueTask<SandboxValue> Combine(
        SandboxValue arg0,
        SandboxValue arg1,
        SandboxValue arg2)
        => ValueTask.FromResult(SandboxValue.FromInt32(
            ((I32Value)arg0).Value * 100 +
            ((I32Value)arg1).Value * 10 +
            ((I32Value)arg2).Value));
}
