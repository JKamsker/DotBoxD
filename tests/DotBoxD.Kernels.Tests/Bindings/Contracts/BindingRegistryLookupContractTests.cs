using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingRegistryLookupContractTests
{
    [Theory]
    [InlineData("contains-null-id", "id")]
    [InlineData("try-get-null-id", "id")]
    [InlineData("get-descriptor-null-id", "id")]
    [InlineData("try-get-null-capability-id", "capabilityId")]
    public void Binding_registry_lookup_methods_reject_null_public_arguments(
        string scenario,
        string expectedParamName)
    {
        var registry = new BindingRegistry([]);

        var ex = Assert.Throws<ArgumentNullException>(() => InvokeNullLookup(registry, scenario));

        Assert.Equal(expectedParamName, ex.ParamName);
    }

    private static void InvokeNullLookup(BindingRegistry registry, string scenario)
    {
        switch (scenario)
        {
            case "contains-null-id":
                _ = registry.Contains(null!);
                return;
            case "try-get-null-id":
                _ = registry.TryGet(null!, out _);
                return;
            case "get-descriptor-null-id":
                _ = registry.GetDescriptor(null!);
                return;
            case "try-get-null-capability-id":
                _ = registry.TryGetCapabilityGrantValidator(null!, out _);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown lookup scenario.");
        }
    }
}
