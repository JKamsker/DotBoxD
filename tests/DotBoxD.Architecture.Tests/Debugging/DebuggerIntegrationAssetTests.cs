using System.Text.Json;
using System.Xml.Linq;

namespace DotBoxD.Architecture.Tests.Debugging;

public sealed class DebuggerIntegrationAssetTests
{
    [Fact]
    public void Vs_code_contribution_registers_pid_attach_and_three_process_compound()
    {
        using var package = Json("ide/vscode-dotboxd-debug/package.json");
        var debugger = Assert.Single(package.RootElement.GetProperty("contributes").GetProperty("debuggers").EnumerateArray());
        Assert.Equal("dotboxd-kernel", debugger.GetProperty("type").GetString());
        Assert.Contains(
            "processId",
            debugger.GetProperty("configurationAttributes").GetProperty("attach").GetProperty("required")
                .EnumerateArray().Select(item => item.GetString()));

        using var launch = Json(".vscode/launch.json");
        var compound = Assert.Single(launch.RootElement.GetProperty("compounds").EnumerateArray());
        Assert.Equal(3, compound.GetProperty("configurations").GetArrayLength());
        Assert.Contains(
            launch.RootElement.GetProperty("configurations").EnumerateArray(),
            item => item.GetProperty("type").GetString() == "dotboxd-kernel");
    }

    [Fact]
    public void Visual_studio_vsix_registers_a_real_attach_launcher_and_packages_the_adapter()
    {
        var root = ArchTestSupport.RepositoryRoot();
        var directory = Path.Combine(root, "ide/visualstudio/DotBoxD.KernelDebug.Vsix");
        var registration = File.ReadAllText(Path.Combine(directory, "AdapterRegistration.pkgdef"));
        var launcher = File.ReadAllText(Path.Combine(directory, "DebugAdapterLauncher.cs"));
        var package = File.ReadAllText(Path.Combine(directory, "DotBoxDKernelAutoAttachPackage.cs"));
        var project = XDocument.Load(Path.Combine(directory, "DotBoxD.KernelDebug.Vsix.csproj"));
        var manifest = XDocument.Load(Path.Combine(directory, "source.extension.vsixmanifest"));

        Assert.Contains("\"Attach\"=dword:00000001", registration, StringComparison.Ordinal);
        Assert.Contains("{80223DBF-71D6-4568-BF29-51F9613ACE15}", registration, StringComparison.Ordinal);
        Assert.Contains("80223DBF-71D6-4568-BF29-51F9613ACE15", launcher, StringComparison.Ordinal);
        Assert.Contains("DBGLAUNCH_BreakOneProcess", package, StringComparison.Ordinal);
        Assert.Contains(
            project.Descendants().Where(item => item.Name.LocalName == "Target"),
            item => item.Attribute("Name")?.Value == "PublishDotBoxDDebugAdapter");
        Assert.Equal(
            "$(VsixIntermediateOutputPath)",
            project.Descendants().Single(item => item.Name.LocalName == "TemplateOutputDirectory").Value);
        Assert.Contains(
            project.Descendants().Where(item => item.Name.LocalName == "Target"),
            item => item.Attribute("Name")?.Value == "PrepareVsixIntermediateOutput" &&
                    item.Attribute("BeforeTargets")?.Value == "GenerateFileManifest");
        Assert.All(
            manifest.Descendants().Where(item => item.Name.LocalName == "InstallationTarget"),
            target => Assert.Equal(
                "amd64",
                target.Elements().Single(item => item.Name.LocalName == "ProductArchitecture").Value));
        Assert.Contains(
            manifest.Descendants().Where(item => item.Name.LocalName == "Asset"),
            item => item.Attribute("Path")?.Value == "AdapterRegistration.pkgdef");
    }

    private static JsonDocument Json(string relativePath)
        => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ArchTestSupport.RepositoryRoot(), relativePath)));
}
