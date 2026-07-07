namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class GeneratedMetadataAttributeContractTests
{
    [Theory]
    [MemberData(nameof(MalformedKernelMethodDescriptorArguments))]
    public void Kernel_method_descriptor_marker_rejects_malformed_required_metadata(
        Action construct,
        Type exceptionType,
        string paramName)
    {
        var exception = Assert.Throws(exceptionType, construct);

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }

    [Theory]
    [MemberData(nameof(MalformedPluginServerRegistryArguments))]
    public void Plugin_server_registry_marker_rejects_malformed_required_metadata(
        Action construct,
        Type exceptionType,
        string paramName)
    {
        var exception = Assert.Throws(exceptionType, construct);

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }

    public static TheoryData<Action, Type, string> MalformedKernelMethodDescriptorArguments()
        => new()
        {
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, null!, "OnDamage", "sig", "hash", "payload"),
                typeof(ArgumentNullException),
                "contextType"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), null!, "sig", "hash", "payload"),
                typeof(ArgumentNullException),
                "methodMetadataName"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "", "sig", "hash", "payload"),
                typeof(ArgumentException),
                "methodMetadataName"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "   ", "sig", "hash", "payload"),
                typeof(ArgumentException),
                "methodMetadataName"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", null!, "hash", "payload"),
                typeof(ArgumentNullException),
                "normalizedSignature"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", "", "hash", "payload"),
                typeof(ArgumentException),
                "normalizedSignature"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", "sig", null!, "payload"),
                typeof(ArgumentNullException),
                "descriptorHash"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", "sig", "", "payload"),
                typeof(ArgumentException),
                "descriptorHash"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", "sig", "hash", null!),
                typeof(ArgumentNullException),
                "descriptorPayload"
            },
            {
                () => new GeneratedKernelMethodDescriptorAttribute(1, typeof(TestContext), "OnDamage", "sig", "hash", ""),
                typeof(ArgumentException),
                "descriptorPayload"
            },
        };

    public static TheoryData<Action, Type, string> MalformedPluginServerRegistryArguments()
        => new()
        {
            {
                () => new GeneratedPluginServerRegistryAttribute(
                    (GeneratedPluginServerRegistryKind)999,
                    typeof(TestServer),
                    typeof(TestContext)),
                typeof(ArgumentOutOfRangeException),
                "kind"
            },
            {
                () => new GeneratedPluginServerRegistryAttribute(
                    GeneratedPluginServerRegistryKind.Hook,
                    null!,
                    typeof(TestContext)),
                typeof(ArgumentNullException),
                "serverType"
            },
            {
                () => new GeneratedPluginServerRegistryAttribute(
                    GeneratedPluginServerRegistryKind.Hook,
                    typeof(TestServer),
                    null!),
                typeof(ArgumentNullException),
                "contextType"
            },
        };

    private sealed class TestContext;

    private sealed class TestServer;
}
