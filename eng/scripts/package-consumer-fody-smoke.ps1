function Add-DotBoxDFodySmokeSource {
    param([Parameter(Mandatory = $true)][string] $ProjectRoot)

    $source = @"
using DotBoxD.Abstractions;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Services.Attributes;

namespace DotBoxD.Kernels.Game.Server.Abstractions
{
    [RpcService]
    public interface IGameWorldAccess
    {
        [HostBinding("smoke.read", "smoke.read", SandboxEffect.Cpu | SandboxEffect.HostStateRead)]
        int Read(int value);
    }
}

namespace DotBoxD.Kernels.Game.Server.Abstractions.Ipc
{
    public readonly record struct LiveSettingUpdate(string Name, string Value);

    [RpcService]
    public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
    {
        ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
        ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
        ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
        ValueTask UpdateSettingsAsync(
            string pluginId,
            LiveSettingUpdate[] updates,
            bool atomic = false,
            CancellationToken ct = default);
        ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
    }
}

namespace DotBoxD.Kernels.Game.Plugin.Client
{
    using DotBoxD.Kernels.Game.Server.Abstractions;

    [GeneratePluginServer(Context = typeof(RemotePluginContext))]
    public partial class RemotePluginServer : IGameWorldAccess;

    public sealed partial class RemotePluginContext;
}

namespace DotBoxD.PackageConsumerSmoke
{
    using DotBoxD.Kernels.Game.Plugin.Client;
    using DotBoxD.Kernels.Game.Server.Abstractions;

    public static class InvokeAsyncUsage
    {
        public static async ValueTask<int> Run(RemotePluginServer server)
        {
            var offset = 3;
            var observed = 0;
            var result = await server.InvokeAsync(async (IGameWorldAccess world) =>
            {
                var value = world.Read(offset);
                observed = value;
                return value;
            });

            return result + observed;
        }
    }
}
"@
    Set-Content -LiteralPath (Join-Path $ProjectRoot "InvokeAsyncSmoke.cs") -Value $source
    Assert-NoDotBoxDFodyFiles -ProjectRoot $ProjectRoot -Phase "consumer setup"
}

function Assert-DotBoxDFodySmokeAssembly {
    param(
        [Parameter(Mandatory = $true)][string] $ProjectRoot,
        [Parameter(Mandatory = $true)][string] $IsolatedPackagesFolder,
        [Parameter(Mandatory = $true)][string] $Configuration)

    Assert-NoDotBoxDFodyFiles -ProjectRoot $ProjectRoot -Phase "package build"
    $cecilAssembly = Get-ChildItem -LiteralPath (Join-Path $IsolatedPackagesFolder "fody") `
        -Filter "Mono.Cecil.dll" -File -Recurse |
        Where-Object { $_.FullName -like "*tasks*netstandard2.0*" } |
        Select-Object -First 1
    if ($null -eq $cecilAssembly) {
        throw "DotBoxD package consumer smoke could not find Fody's Mono.Cecil assembly."
    }

    Add-Type -Path $cecilAssembly.FullName
    $assemblyPath = Join-Path $ProjectRoot "bin/$Configuration/net10.0/DotBoxD.PluginAuthoringSmoke.dll"
    $module = [Mono.Cecil.ModuleDefinition]::ReadModule($assemblyPath)
    try {
        $generatedType = $module.GetType("DotBoxD.Plugins.Generated.InvokeAsyncInterceptors")
        if ($null -eq $generatedType) {
            throw "The package consumer did not generate InvokeAsync interceptors."
        }

        $methods = Get-DotBoxDGeneratedMethods -GeneratedType $generatedType
        $captureHelperCalls = @($methods |
            ForEach-Object { $_.Body.Instructions } |
            Where-Object {
                $_.Operand -is [Mono.Cecil.MethodReference] -and
                $_.Operand.DeclaringType.FullName -eq $generatedType.FullName -and
                $_.Operand.Name -in @("__ReadCapture", "__WriteCapture")
            })
        if ($captureHelperCalls.Count -ne 0) {
            throw "The packaged DotBoxD weaver left reflection capture helper calls in generated IL."
        }

        $captureFields = @($methods |
            ForEach-Object { $_.Body.Instructions } |
            Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] } |
            ForEach-Object { $_.Operand.Name })
        foreach ($expectedField in @("offset", "observed")) {
            if (-not ($captureFields | Where-Object { $_.Contains($expectedField, [StringComparison]::Ordinal) })) {
                throw "The packaged DotBoxD weaver did not emit direct access to capture '$expectedField'."
            }
        }
    } finally {
        $module.Dispose()
    }
}

function Assert-DotBoxDFodyConfigurationFailure {
    param(
        [Parameter(Mandatory = $true)][string] $ProjectRoot,
        [Parameter(Mandatory = $true)][string] $Configuration)

    $customConfigurationPath = Join-Path $ProjectRoot "Directory.Build.targets"
    $customConfiguration = @"
<Project>
  <PropertyGroup>
    <WeaverConfiguration>
      <Weavers>
        <Consumer.Custom />
      </Weavers>
    </WeaverConfiguration>
  </PropertyGroup>
</Project>
"@
    Set-Content -LiteralPath $customConfigurationPath -Value $customConfiguration
    try {
        $validationOutput = dotnet build $ProjectRoot --configuration $Configuration --no-restore 2>&1
        if ($LASTEXITCODE -eq 0) {
            throw "DotBoxD package build silently accepted a custom WeaverConfiguration that disabled its weaver."
        }

        $expectedText = "existing WeaverConfiguration does not contain <DotBoxD.Plugins />"
        if (-not (($validationOutput | Out-String).Contains($expectedText, [StringComparison]::Ordinal))) {
            throw "DotBoxD package build did not report the actionable missing-weaver configuration error."
        }
    } finally {
        Remove-Item -LiteralPath $customConfigurationPath -Force
    }
}

function Assert-NoDotBoxDFodyFiles {
    param(
        [Parameter(Mandatory = $true)][string] $ProjectRoot,
        [Parameter(Mandatory = $true)][string] $Phase)

    if (Get-ChildItem -LiteralPath $ProjectRoot -Filter "FodyWeavers.*" -File -Recurse) {
        throw "DotBoxD $Phase must not require or generate a visible FodyWeavers file."
    }
}

function Get-DotBoxDGeneratedMethods {
    param([Parameter(Mandatory = $true)] $GeneratedType)

    $methods = [System.Collections.Generic.List[Mono.Cecil.MethodDefinition]]::new()
    $pendingTypes = [System.Collections.Generic.Stack[Mono.Cecil.TypeDefinition]]::new()
    $pendingTypes.Push($GeneratedType)
    while ($pendingTypes.Count -gt 0) {
        $type = $pendingTypes.Pop()
        foreach ($method in $type.Methods) {
            if ($method.HasBody) {
                $methods.Add($method)
            }
        }

        foreach ($nestedType in $type.NestedTypes) {
            $pendingTypes.Push($nestedType)
        }
    }

    return $methods
}
