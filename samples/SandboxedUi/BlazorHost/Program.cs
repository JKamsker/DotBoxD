using Examples.SandboxedUi.BlazorHost;
using Examples.SandboxedUi.BlazorHost.Components;

var smoke = args.Contains("--smoke", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--smoke").ToArray());
builder.WebHost.UseStaticWebAssets();
var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
var worker = builder.Configuration["worker"] ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../Plugin/bin", configuration, "net10.0/Examples.SandboxedUi.Plugin.dll"));
if (smoke)
{
    await BlazorSmoke.RunAsync(worker);
    return;
}
builder.Services.AddSingleton(new WorkerPath(worker));
builder.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 64 * 1024);
var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
await app.RunAsync();

namespace Examples.SandboxedUi.BlazorHost
{
    internal sealed record WorkerPath(string Value);
}
