using System.Text.Json;
using DotBoxD.Services.SourceGenerator.Tests.Generation;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class DtoPayloadIgnoreAttributeTests
{
    [Fact]
    public async Task JsonIgnoredDerivedGetter_RoundTripsThroughGeneratedCodeWithoutSerializedGetter()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace Regress.DtoPayloadIgnoreRuntime
            {
                public readonly record struct ResourceHandle(ulong Value)
                {
                    [JsonIgnore]
                    public bool IsEmpty => Value == 0;
                }

                [RpcService]
                public interface IResourceRpc
                {
                    Task<ResourceHandle> EchoAsync(ResourceHandle handle);
                }

                public sealed class ResourceRpc : IResourceRpc
                {
                    public Task<ResourceHandle> EchoAsync(ResourceHandle handle) => Task.FromResult(handle);
                }
            }
            """;

        var h = GeneratedRoundTripTestSupport.Harness.Build(
            source,
            "Regress.DtoPayloadIgnoreRuntime.IResourceRpc",
            "Regress.DtoPayloadIgnoreRuntime.ResourceRpc");
        var handleType = h.LoadType("Regress.DtoPayloadIgnoreRuntime.ResourceHandle");
        var handle = Activator.CreateInstance(handleType, 0UL)!;

        var json = JsonSerializer.Serialize(handle, handleType);
        json.Should().Contain("Value");
        json.Should().NotContain("IsEmpty");

        var echoed = (await h.CallAsync("EchoAsync", handle))!;
        echoed.GetType().GetProperty("Value")!.GetValue(echoed).Should().Be(0UL);
        echoed.GetType().GetProperty("IsEmpty")!.GetValue(echoed).Should().Be(true);
    }

    [Fact]
    public void SerializerIgnoredDerivedGetters_AreSkippedByDtoReconstructibilityValidation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace MessagePack
            {
                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
                public sealed class IgnoreMemberAttribute : Attribute
                {
                }
            }

            namespace Regress.DtoPayloadIgnore
            {
                public readonly record struct JsonHandle(ulong Value)
                {
                    [JsonIgnore]
                    public bool IsEmpty => Value == 0;
                }

                public readonly record struct DataMemberHandle(ulong Value)
                {
                    [IgnoreDataMember]
                    public bool IsEmpty => Value == 0;
                }

                public readonly record struct MessagePackHandle(ulong Value)
                {
                    [MessagePack.IgnoreMember]
                    public bool IsEmpty => Value == 0;
                }

                [RpcService]
                public interface IResourceRpc
                {
                    Task<int> SendJsonAsync(JsonHandle handle);
                    Task<int> SendDataMemberAsync(DataMemberHandle handle);
                    Task<int> SendMessagePackAsync(MessagePackHandle handle);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().BeEmpty();

        var generated = runResult.Results.Single().GeneratedSources;
        var proxy = generated
            .Single(g => g.HintName.EndsWith("IResourceRpc.DotBoxDRpcProxy.g.cs"))
            .SourceText
            .ToString();
        proxy.Should().NotContain("throw new global::System.NotSupportedException");

        var dispatcher = generated
            .Single(g => g.HintName.EndsWith("IResourceRpc.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendJsonAsync\":");
        dispatcher.Should().Contain("case \"SendDataMemberAsync\":");
        dispatcher.Should().Contain("case \"SendMessagePackAsync\":");
    }

    [Fact]
    public void SerializerIgnoredUnsupportedMembers_AreSkippedByNestedDtoValidation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System;
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace MessagePack
            {
                [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
                public sealed class IgnoreMemberAttribute : Attribute
                {
                }
            }

            namespace Regress.DtoPayloadIgnore
            {
                public sealed class JsonIgnoredTaskRequest
                {
                    public int Value { get; init; }

                    [JsonIgnore]
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                public sealed class DataMemberIgnoredTaskRequest
                {
                    public int Value { get; init; }

                    [IgnoreDataMember]
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                public sealed class MessagePackIgnoredTaskRequest
                {
                    public int Value { get; init; }

                    [MessagePack.IgnoreMember]
                    public Task<int> Work { get; init; } = Task.FromResult(0);
                }

                [RpcService]
                public interface IIgnoredUnsupportedPayloads
                {
                    Task<int> SendJsonAsync(JsonIgnoredTaskRequest request);
                    Task<int> SendDataMemberAsync(DataMemberIgnoredTaskRequest request);
                    Task<int> SendMessagePackAsync(MessagePackIgnoredTaskRequest request);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Where(d => d.Id == "DBXS002").Should().BeEmpty();

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IIgnoredUnsupportedPayloads.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().Contain("case \"SendJsonAsync\":");
        dispatcher.Should().Contain("case \"SendDataMemberAsync\":");
        dispatcher.Should().Contain("case \"SendMessagePackAsync\":");
    }

    [Fact]
    public void UnignoredDerivedGetter_RemainsRejectedByDtoReconstructibilityValidation()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Threading.Tasks;

            namespace Regress.DtoPayloadIgnore
            {
                public readonly record struct ResourceHandle(ulong Value)
                {
                    public bool IsEmpty => Value == 0;
                }

                [RpcService]
                public interface IRejectedResourceRpc
                {
                    Task<int> SendAsync(ResourceHandle handle);
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostic = runResult.Diagnostics.Should().ContainSingle(d => d.Id == "DBXS002").Subject;
        diagnostic.GetMessage().Should().Contain("member 'IsEmpty'");
        diagnostic.GetMessage().Should().Contain("public setter or init");

        var dispatcher = runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith("IRejectedResourceRpc.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();
        dispatcher.Should().NotContain("case \"SendAsync\":");
    }

    private static GeneratorDriverRunResult Compile(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);

        using var ms = new MemoryStream();
        var emit = finalCompilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(
            "\n",
            emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())));

        return runResult;
    }
}
