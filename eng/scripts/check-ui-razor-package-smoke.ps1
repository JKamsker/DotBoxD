param([string] $PackageDirectory = "artifacts/packages")

$ErrorActionPreference = "Stop"
$packages = (Resolve-Path -LiteralPath $PackageDirectory).Path
$razorPackages = @(Get-ChildItem -LiteralPath $packages -Filter "DotBoxD.UI.Razor.*.nupkg" -File)
if ($razorPackages.Count -ne 1 -or $razorPackages[0].Name -notmatch '^DotBoxD\.UI\.Razor\.(?<version>.+)\.nupkg$') {
    throw "Expected exactly one safe Razor package."
}
$version = $Matches.version
$temporary = Join-Path ([System.IO.Path]::GetTempPath()) ("dotboxd-razor-consumer-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $feed = [System.Security.SecurityElement]::Escape($packages)
    Set-Content -LiteralPath (Join-Path $temporary "NuGet.Config") -Encoding utf8 -Value @"
<configuration><packageSources><clear/><add key="local" value="$feed"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
"@
    Set-Content -LiteralPath (Join-Path $temporary "Consumer.csproj") -Encoding utf8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD.UI" Version="$version" />
    <PackageReference Include="DotBoxD.UI.Razor" Version="$version" PrivateAssets="all" />
    <PackageReference Include="DotBoxD.Plugins.Analyzer" Version="$version" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@
    Set-Content -LiteralPath (Join-Path $temporary "Counter.ui.razor") -Encoding utf8 -Value @'
@state int count = 0;
@kernel increment = Ui.Bind(Increment, count);
<UiVertical>
    <UiText Text="NuGet-authored safe Razor" />
    <UiButton Text="Increment" OnClick="@Ui.Handle(increment, count)" />
    <UiButton Text="Search" OnClick="@Ui.Remote(SearchUiEndpoint)" />
</UiVertical>
'@
    Set-Content -LiteralPath (Join-Path $temporary "Program.cs") -Encoding utf8 -Value @'
using DotBoxD.UI;
using DotBoxD.UI.Authoring;
var builder = new UiBuilder();
var package = builder.Build(new Counter().Render(builder));
if (package.Nodes.Length != 4 || package.Kernels.Length != 1 || package.Events.Length != 2 ||
    package.Events[0].Target != UiEventTarget.LocalKernel || package.Events[1].RemoteEndpointId != 7)
{ throw new InvalidOperationException("The published authoring packages did not produce the shared schema."); }
Console.WriteLine("PASS: NuGet safe Razor discovery, public builder composition, restricted kernel and explicit remote endpoint.");
[UiRazorComponent("Counter.ui.razor")]
public partial class Counter
{
    [UiLocalHandler] private static int Increment(int value) => value + 1;
    [UiRemoteHandler(7)] private static string Search(string query) => File.ReadAllText(query);
}
'@
    & dotnet run --project (Join-Path $temporary "Consumer.csproj") --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "Safe Razor NuGet consumer failed with exit code $LASTEXITCODE." }
} finally {
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
