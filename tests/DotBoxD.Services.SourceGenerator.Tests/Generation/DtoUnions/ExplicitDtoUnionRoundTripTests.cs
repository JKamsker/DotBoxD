using System.Collections;
using FluentAssertions;
using static DotBoxD.Services.SourceGenerator.Tests.Generation.GeneratedRoundTripTestSupport;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation.DtoUnions;

public sealed class ExplicitDtoUnionRoundTripTests
{
    [Fact]
    public async Task Generated_rpc_round_trips_json_polymorphic_dto_union()
    {
        const string source = """
            #nullable enable
            using DotBoxD.Services.Attributes;
            using System;
            using System.Collections.Generic;
            using System.Text.Json.Serialization;
            using System.Threading.Tasks;

            namespace Regress.ExplicitDtoUnions.RoundTrip
            {
                [RpcService]
                public interface ILayoutRpc
                {
                    Task<int> ApplyLayoutAsync(LayoutSpec spec);
                }

                public sealed class LayoutRpc : ILayoutRpc
                {
                    public LayoutSpec? Last { get; private set; }

                    public Task<int> ApplyLayoutAsync(LayoutSpec spec)
                    {
                        Last = spec;
                        return Task.FromResult(spec.Widgets.Count);
                    }
                }

                public sealed class LayoutSpec
                {
                    public string Id { get; set; } = "";
                    public IReadOnlyList<WidgetSpec> Widgets { get; set; } = Array.Empty<WidgetSpec>();
                }

                [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
                [JsonDerivedType(typeof(TextWidgetSpec), "text")]
                [JsonDerivedType(typeof(PanelWidgetSpec), "panel")]
                public abstract class WidgetSpec
                {
                    public string Id { get; set; } = "";
                }

                public sealed class TextWidgetSpec : WidgetSpec
                {
                    public string Text { get; set; } = "";
                }

                public sealed class PanelWidgetSpec : WidgetSpec
                {
                    public IReadOnlyList<WidgetSpec> Children { get; set; } = Array.Empty<WidgetSpec>();
                }
            }
            """;

        var harness = Harness.Build(
            source,
            "Regress.ExplicitDtoUnions.RoundTrip.ILayoutRpc",
            "Regress.ExplicitDtoUnions.RoundTrip.LayoutRpc");

        var layout = CreateLayout(harness);
        var count = await harness.CallAsync("ApplyLayoutAsync", layout);

        count.Should().Be(2);
        var last = harness.GetImplProperty("Last");
        last.Should().NotBeNull();
        WidgetTypeNames(last!).Should().Equal("TextWidgetSpec", "PanelWidgetSpec");
    }

    private static object CreateLayout(Harness harness)
    {
        var widgetType = harness.LoadType("Regress.ExplicitDtoUnions.RoundTrip.WidgetSpec");
        var textType = harness.LoadType("Regress.ExplicitDtoUnions.RoundTrip.TextWidgetSpec");
        var panelType = harness.LoadType("Regress.ExplicitDtoUnions.RoundTrip.PanelWidgetSpec");
        var layoutType = harness.LoadType("Regress.ExplicitDtoUnions.RoundTrip.LayoutSpec");
        var widgets = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(widgetType))!;

        var text = Activator.CreateInstance(textType)!;
        Set(text, "Id", "title");
        Set(text, "Text", "Deploy");
        widgets.Add(text);

        var panel = Activator.CreateInstance(panelType)!;
        Set(panel, "Id", "panel");
        widgets.Add(panel);

        var layout = Activator.CreateInstance(layoutType)!;
        Set(layout, "Id", "main");
        Set(layout, "Widgets", widgets);
        return layout;
    }

    private static string[] WidgetTypeNames(object layout)
    {
        var widgets = (IEnumerable)layout.GetType().GetProperty("Widgets")!.GetValue(layout)!;
        return widgets.Cast<object>().Select(widget => widget.GetType().Name).ToArray();
    }

    private static void Set(object instance, string property, object value) =>
        instance.GetType().GetProperty(property)!.SetValue(instance, value);
}
