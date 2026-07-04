using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingRegistryBuilderAtomicityTests
{
    [Fact]
    public void AddRange_rejects_null_descriptor_without_adding_previous_descriptors()
    {
        var builder = new BindingRegistryBuilder();
        var first = TestBinding("test.first");

        var ex = Assert.Throws<ArgumentException>(() => builder.AddRange([first, null!]));

        Assert.Equal("descriptors", ex.ParamName);
        var registry = builder.Add(TestBinding("test.second")).Build();
        Assert.False(registry.Contains("test.first"));
        Assert.True(registry.Contains("test.second"));
    }

    private static BindingDescriptor TestBinding(string id)
        => new(
            id,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
