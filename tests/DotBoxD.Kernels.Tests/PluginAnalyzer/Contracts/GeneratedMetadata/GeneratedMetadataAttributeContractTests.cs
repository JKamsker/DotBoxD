namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Contracts;

public sealed class GeneratedMetadataAttributeContractTests
{
    [Theory]
    [MemberData(nameof(MalformedKernelMethodDescriptorArguments))]
    public void Kernel_method_descriptor_marker_rejects_malformed_required_metadata(
        int schemaVersion,
        Type contextType,
        string methodMetadataName,
        string normalizedSignature,
        string descriptorHash,
        string descriptorPayload,
        Type exceptionType,
        string paramName)
    {
        AssertThrowsWithParamName(
            () => new GeneratedKernelMethodDescriptorAttribute(
                schemaVersion,
                contextType,
                methodMetadataName,
                normalizedSignature,
                descriptorHash,
                descriptorPayload),
            exceptionType,
            paramName);
    }

    [Theory]
    [MemberData(nameof(MalformedPluginServerRegistryArguments))]
    public void Plugin_server_registry_marker_rejects_malformed_required_metadata(
        GeneratedPluginServerRegistryKind kind,
        Type serverType,
        Type contextType,
        Type exceptionType,
        string paramName)
    {
        AssertThrowsWithParamName(
            () => new GeneratedPluginServerRegistryAttribute(kind, serverType, contextType),
            exceptionType,
            paramName);
    }

    public static TheoryData<int, Type, string, string, string, string, Type, string> MalformedKernelMethodDescriptorArguments()
        => new()
        {
            {
                1,
                null!,
                "OnDamage",
                "sig",
                "hash",
                "payload",
                typeof(ArgumentNullException),
                "contextType"
            },
            {
                1,
                typeof(TestContext),
                null!,
                "sig",
                "hash",
                "payload",
                typeof(ArgumentNullException),
                "methodMetadataName"
            },
            {
                1,
                typeof(TestContext),
                "",
                "sig",
                "hash",
                "payload",
                typeof(ArgumentException),
                "methodMetadataName"
            },
            {
                1,
                typeof(TestContext),
                "   ",
                "sig",
                "hash",
                "payload",
                typeof(ArgumentException),
                "methodMetadataName"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                null!,
                "hash",
                "payload",
                typeof(ArgumentNullException),
                "normalizedSignature"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                "",
                "hash",
                "payload",
                typeof(ArgumentException),
                "normalizedSignature"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                "sig",
                null!,
                "payload",
                typeof(ArgumentNullException),
                "descriptorHash"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                "sig",
                "",
                "payload",
                typeof(ArgumentException),
                "descriptorHash"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                "sig",
                "hash",
                null!,
                typeof(ArgumentNullException),
                "descriptorPayload"
            },
            {
                1,
                typeof(TestContext),
                "OnDamage",
                "sig",
                "hash",
                "",
                typeof(ArgumentException),
                "descriptorPayload"
            },
        };

    public static TheoryData<GeneratedPluginServerRegistryKind, Type, Type, Type, string> MalformedPluginServerRegistryArguments()
        => new()
        {
            {
                (GeneratedPluginServerRegistryKind)999,
                typeof(TestServer),
                typeof(TestContext),
                typeof(ArgumentOutOfRangeException),
                "kind"
            },
            {
                GeneratedPluginServerRegistryKind.Hook,
                null!,
                typeof(TestContext),
                typeof(ArgumentNullException),
                "serverType"
            },
            {
                GeneratedPluginServerRegistryKind.Hook,
                typeof(TestServer),
                null!,
                typeof(ArgumentNullException),
                "contextType"
            },
        };

    private static void AssertThrowsWithParamName(Action construct, Type exceptionType, string paramName)
    {
        var exception = Assert.Throws(exceptionType, construct);

        var argumentException = Assert.IsAssignableFrom<ArgumentException>(exception);
        Assert.Equal(paramName, argumentException.ParamName);
    }

    private sealed class TestContext;

    private sealed class TestServer;
}
