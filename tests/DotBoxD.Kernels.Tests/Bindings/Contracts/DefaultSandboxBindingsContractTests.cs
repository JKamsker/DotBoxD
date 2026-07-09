using DotBoxD.Kernels.Runtime.Bindings;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class DefaultSandboxBindingsContractTests
{
    [Theory]
    [MemberData(nameof(NullBuilderCases))]
    public void Default_binding_extension_methods_reject_null_builders(
        string methodName,
        Action addBindings)
    {
        var ex = Record.Exception(addBindings);

        Assert.NotNull(ex);
        Assert.False(
            ex is NullReferenceException,
            $"{methodName} should reject a null builder at the public boundary.");
        var argumentNull = Assert.IsType<ArgumentNullException>(ex);
        Assert.Equal("builder", argumentNull.ParamName);
    }

    public static TheoryData<string, Action> NullBuilderCases => new()
    {
        {
            nameof(DefaultSandboxBindings.AddDefaultPureBindings),
            () => DefaultSandboxBindings.AddDefaultPureBindings(null!)
        },
        {
            nameof(DefaultSandboxBindings.AddFileBindings),
            () => DefaultSandboxBindings.AddFileBindings(null!)
        },
        {
            nameof(DefaultSandboxBindings.AddTimeBindings),
            () => DefaultSandboxBindings.AddTimeBindings(null!)
        },
        {
            nameof(DefaultSandboxBindings.AddRandomBindings),
            () => DefaultSandboxBindings.AddRandomBindings(null!)
        },
        {
            nameof(DefaultSandboxBindings.AddLogBindings),
            () => DefaultSandboxBindings.AddLogBindings(null!)
        }
    };
}
