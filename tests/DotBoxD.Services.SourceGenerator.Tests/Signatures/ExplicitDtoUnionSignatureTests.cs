using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Signatures;

public sealed class ExplicitDtoUnionSignatureTests
{
    [Fact]
    public void MessagePackUnionDto_AllowsAbstractRecursiveListMember()
    {
        const string source = AttributeStubs.MessagePackUnion + """

            namespace Regress.ExplicitDtoUnions.MsgPack
            {
                using DotBoxD.Services.Attributes;
                using MessagePack;
                using System;
                using System.Collections.Generic;
                using System.Threading.Tasks;

                public sealed class LayoutSpec
                {
                    public string Id { get; init; } = "";
                    public IReadOnlyList<WidgetSpec> Widgets { get; init; } = Array.Empty<WidgetSpec>();
                }

                [Union(0, typeof(TextWidgetSpec))]
                [Union(1, typeof(PanelWidgetSpec))]
                public abstract class WidgetSpec
                {
                    public string Id { get; init; } = "";
                }

                public sealed class TextWidgetSpec : WidgetSpec
                {
                    public string Text { get; init; } = "";
                }

                public sealed class PanelWidgetSpec : WidgetSpec
                {
                    public IReadOnlyList<WidgetSpec> Children { get; init; } = Array.Empty<WidgetSpec>();
                }

                [RpcService]
                public interface ILayoutRpc
                {
                    Task ApplyLayoutAsync(LayoutSpec spec);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");
        Dispatcher(runResult, "ILayoutRpc").Should().Contain("case \"ApplyLayoutAsync\":");
    }

    [Fact]
    public void JsonUnionDto_AllowsInterfaceMember()
    {
        const string source = """
            using DotBoxD.Services.Attributes;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace Regress.ExplicitDtoUnions.Json
            {
                public sealed class LayoutSpec
                {
                    public IWidgetSpec Root { get; init; } = new TextWidgetSpec();
                }

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                [JsonDerivedType(typeof(TextWidgetSpec), "text")]
                [JsonDerivedType(typeof(ActionWidgetSpec), "action")]
                public interface IWidgetSpec
                {
                    string Id { get; }
                }

                public sealed class TextWidgetSpec : IWidgetSpec
                {
                    public string Id { get; init; } = "";
                    public string Text { get; init; } = "";
                }

                public sealed class ActionWidgetSpec : IWidgetSpec
                {
                    public string Id { get; init; } = "";
                    public string Command { get; init; } = "";
                }

                [RpcService]
                public interface ILayoutRpc
                {
                    Task ApplyLayoutAsync(LayoutSpec spec);
                }
            }
            """;

        var runResult = Compile(source);

        runResult.Diagnostics.Should().NotContain(d => d.Id == "DBXS002");
        Dispatcher(runResult, "ILayoutRpc").Should().Contain("case \"ApplyLayoutAsync\":");
    }

    [Fact]
    public void InvalidExplicitDtoUnions_ProduceDBXS002_AndSkipDispatch()
    {
        const string source = AttributeStubs.MessagePackUnion + """

            namespace Regress.ExplicitDtoUnions.Invalid
            {
                using DotBoxD.Services.Attributes;
                using MessagePack;
                using System.Text.Json.Serialization;
                using System.Threading.Tasks;

                public sealed class DuplicateEnvelope
                {
                    public DuplicateWidget Widget { get; init; } = null!;
                }

                [Union(0, typeof(DuplicateTextWidget))]
                [Union(0, typeof(DuplicateActionWidget))]
                public abstract class DuplicateWidget;

                public sealed class DuplicateTextWidget : DuplicateWidget;
                public sealed class DuplicateActionWidget : DuplicateWidget;

                public sealed class MissingDiscriminatorEnvelope
                {
                    public MissingDiscriminatorWidget Widget { get; init; } = null!;
                }

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                [JsonDerivedType(typeof(MissingDiscriminatorTextWidget))]
                public abstract class MissingDiscriminatorWidget;

                public sealed class MissingDiscriminatorTextWidget : MissingDiscriminatorWidget;

                public sealed class StringNamedEnvelope
                {
                    public StringNamedWidget Widget { get; init; } = null!;
                }

                [Union(0, "Regress.ExplicitDtoUnions.Invalid.StringNamedTextWidget")]
                public abstract class StringNamedWidget;

                public sealed class StringNamedTextWidget : StringNamedWidget;

                public sealed class NonConcreteEnvelope
                {
                    public NonConcreteWidget Widget { get; init; } = null!;
                }

                [JsonDerivedType(typeof(AbstractWidgetCase), "abstract")]
                public abstract class NonConcreteWidget;

                public abstract class AbstractWidgetCase : NonConcreteWidget;

                public sealed class UnreconstructibleEnvelope
                {
                    public UnreconstructibleWidget Widget { get; init; } = null!;
                }

                [Union(0, typeof(UnreconstructibleTextWidget))]
                public abstract class UnreconstructibleWidget;

                public sealed class UnreconstructibleTextWidget : UnreconstructibleWidget
                {
                    public string Text { get; private init; } = "";
                }

                public sealed class IncompleteEnvelope
                {
                    public IncompleteWidget Widget { get; init; } = null!;
                }

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                public abstract class IncompleteWidget;

                [RpcService]
                public interface IInvalidLayoutRpc
                {
                    Task SendDuplicateAsync(DuplicateEnvelope request);
                    Task SendMissingDiscriminatorAsync(MissingDiscriminatorEnvelope request);
                    Task SendStringNamedAsync(StringNamedEnvelope request);
                    Task SendNonConcreteAsync(NonConcreteEnvelope request);
                    Task SendUnreconstructibleAsync(UnreconstructibleEnvelope request);
                    Task SendIncompleteAsync(IncompleteEnvelope request);
                }
            }
            """;

        var runResult = Compile(source);

        var diagnostics = runResult.Diagnostics.Where(d => d.Id == "DBXS002").ToArray();
        diagnostics.Should().HaveCount(6);
        diagnostics.Should().Contain(d => d.GetMessage().Contains("duplicate") &&
            d.GetMessage().Contains("discriminator"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("stable discriminator"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("Type-based overload"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("concrete"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("public setter or init"));
        diagnostics.Should().Contain(d => d.GetMessage().Contains("at least one derived DTO type"));

        var dispatcher = Dispatcher(runResult, "IInvalidLayoutRpc");
        dispatcher.Should().NotContain("case \"SendDuplicateAsync\":");
        dispatcher.Should().NotContain("case \"SendMissingDiscriminatorAsync\":");
        dispatcher.Should().NotContain("case \"SendStringNamedAsync\":");
        dispatcher.Should().NotContain("case \"SendNonConcreteAsync\":");
        dispatcher.Should().NotContain("case \"SendUnreconstructibleAsync\":");
        dispatcher.Should().NotContain("case \"SendIncompleteAsync\":");
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

    private static string Dispatcher(GeneratorDriverRunResult runResult, string interfaceName) =>
        runResult.Results.Single()
            .GeneratedSources
            .Single(g => g.HintName.EndsWith($"{interfaceName}.DotBoxDRpcDispatcher.g.cs"))
            .SourceText
            .ToString();

    private static class AttributeStubs
    {
        public const string MessagePackUnion = """
            namespace MessagePack
            {
                [System.AttributeUsage(
                    System.AttributeTargets.Class | System.AttributeTargets.Interface,
                    AllowMultiple = true,
                    Inherited = false)]
                public sealed class UnionAttribute : System.Attribute
                {
                    public UnionAttribute(int key, System.Type subType)
                    {
                    }

                    public UnionAttribute(int key, string subType)
                    {
                    }
                }
            }
            """;
    }
}
