param(
    [string] $PackageDirectory = "artifacts/packages",
    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packageRoot = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) {
    [System.IO.Path]::GetFullPath($PackageDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $root $PackageDirectory))
}
$workRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts/netstandard-package-smoke"))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))
if (-not $workRoot.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean netstandard smoke directory outside artifacts: $workRoot"
}

Remove-Item -LiteralPath $workRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $workRoot | Out-Null

$escapedPackageRoot = [System.Security.SecurityElement]::Escape($packageRoot)
[System.IO.File]::WriteAllText((Join-Path $workRoot "NuGet.Config"), @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="dotboxd-local" value="$escapedPackageRoot" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
"@)

[System.IO.File]::WriteAllText((Join-Path $workRoot "NetStandardSmoke.csproj"), @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotBoxD.Services" Version="$ExpectedVersion" />
    <PackageReference Include="DotBoxD.Codecs.MessagePack" Version="$ExpectedVersion" />
    <PackageReference Include="DotBoxD.Transports.Tcp" Version="$ExpectedVersion" />
    <PackageReference Include="DotBoxD.Transports.NamedPipes" Version="$ExpectedVersion" />
    <PackageReference Include="MessagePack" Version="3.1.7" />
  </ItemGroup>
</Project>
"@)

[System.IO.File]::WriteAllText((Join-Path $workRoot "NetStandardSmoke.cs"), @'
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Services.Attributes;
using DotBoxD.Services.Serialization;
using DotBoxD.Services.Transport;
using DotBoxD.Transports.NamedPipes;
using DotBoxD.Transports.Tcp;
using MessagePack.Resolvers;

[RpcService]
public interface INetStandardSmokeService
{
    ValueTask<string> EchoAsync(string value, CancellationToken cancellationToken = default);
}

public static class NetStandardSmoke
{
    public static ISerializer CreateSerializer()
        => MessagePackRpcSerializer.CreateWithResolver(BuiltinResolver.Instance);

    public static ITransport CreateTcpTransport()
        => new TcpTransport("localhost", 5000);

    public static ITransport CreateNamedPipeTransport()
        => new NamedPipeClientTransport("dotboxd-netstandard-smoke");
}
'@)

$project = Join-Path $workRoot "NetStandardSmoke.csproj"
$config = Join-Path $workRoot "NuGet.Config"
& dotnet restore $project --configfile $config `
    -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
if ($LASTEXITCODE -ne 0) {
    throw "Failed to restore the netstandard2.1 packed-package consumer."
}

& dotnet build $project -c $Configuration --no-restore -warnaserror `
    -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false
if ($LASTEXITCODE -ne 0) {
    throw "Failed to compile the netstandard2.1 packed-package consumer."
}

Write-Host "netstandard2.1 Services, MessagePack, TCP, and named-pipe package smoke passed."
