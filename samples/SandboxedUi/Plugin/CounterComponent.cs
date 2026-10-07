using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using Examples.SandboxedUi.Contracts;

namespace Examples.SandboxedUi.Plugin;

internal sealed partial class CounterComponent : IUiComponent
{
    public static UiPackage Package()
    {
        var builder = new UiBuilder();
        return builder.Build(new CounterComponent().Render(builder));
    }

    public UiElement Render(UiBuilder builder)
    {
        var count = builder.State(0);
        var query = builder.State("");
        var results = builder.State("");
        var score = builder.State(0);
        var increment = builder.Kernel(IncrementUiKernel(), count);
        var label = builder.Kernel(LabelUiKernel(), count);
        var readScore = builder.Kernel(ReadScoreUiKernel());
        return builder.Border(builder.Stack(
            builder.Text(label), builder.Button("Increment", increment, count),
            new SearchComponent(query, results).Render(builder),
            builder.Button("Read game score", readScore, score)));
    }

    [UiLocalHandler]
    private static int Increment(int count) => count + 1;

    [UiLocalHandler]
    private static string Label(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [UiLocalHandler]
    private static int ReadScore() => GameBindings.ReadScore();
}

internal sealed class SearchComponent(UiState<string> query, UiState<string> results) : IUiComponent
{
    public UiElement Render(UiBuilder builder) => builder.Stack(builder.TextBox(query),
        builder.RemoteButton("Search", UiPlugin.SearchAsyncUiEndpoint), builder.Text(results));
}
