using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Simple;

namespace Examples.SandboxedUi.Host;

public sealed class SampleApplication : Application
{
    public SampleApplication() => Styles.Add(new SimpleTheme());
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<SampleApplication>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
