using DotBoxD.UI;
using DotBoxD.UI.Authoring;
using Examples.SandboxedUi.Contracts;

namespace Examples.SandboxedUi.Plugin;

[UiRazorComponent("Counter.ui.razor")]
internal sealed partial class CounterComponent : IUiComponent
{
    public static UiPackage Package()
    {
        var builder = new UiBuilder();
        return builder.Build(new CounterComponent().Render(builder));
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
