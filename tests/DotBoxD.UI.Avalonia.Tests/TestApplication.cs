using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Simple;

[assembly: AvaloniaTestApplication(typeof(DotBoxD.UI.Avalonia.Tests.TestApplication))]

namespace DotBoxD.UI.Avalonia.Tests;

public sealed class TestApplication : Application
{
    public TestApplication() => Styles.Add(new SimpleTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
