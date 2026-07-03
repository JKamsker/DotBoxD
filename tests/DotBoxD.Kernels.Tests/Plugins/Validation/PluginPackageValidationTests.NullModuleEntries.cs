using System.Reflection;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;

namespace DotBoxD.Kernels.Tests.Plugins;

public sealed partial class PluginPackageValidationTests
{
    [Fact]
    public async Task Install_rejects_null_module_capability_request_entries()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Module = package.Module with
            {
                CapabilityRequests = [.. package.Module.CapabilityRequests, null!]
            }
        };

        await AssertInstallValidationDiagnosticAsync(invalid, "capabilityRequests");
    }

    [Fact]
    public async Task Install_rejects_null_module_function_entries()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = package with
        {
            Module = package.Module with
            {
                Functions = [.. package.Module.Functions, null!]
            }
        };

        await AssertInstallValidationDiagnosticAsync(invalid, "functions");
    }

    [Fact]
    public async Task Install_rejects_null_module_capability_request_collection()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = WithNullModuleCollection(package, "_capabilityRequests");

        await AssertInstallValidationDiagnosticAsync(invalid, "capabilityRequests");
    }

    [Fact]
    public async Task Install_rejects_null_module_function_collection()
    {
        var package = FireDamagePluginPackage.Create();
        var invalid = WithNullModuleCollection(package, "_functions");

        await AssertInstallValidationDiagnosticAsync(invalid, "functions");
    }

    private static PluginPackage WithNullModuleCollection(PluginPackage package, string fieldName)
    {
        var module = package.Module with { };
        var field = typeof(SandboxModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"SandboxModule field '{fieldName}' was not found.");
        field.SetValue(module, null);
        return package with { Module = module };
    }
}
