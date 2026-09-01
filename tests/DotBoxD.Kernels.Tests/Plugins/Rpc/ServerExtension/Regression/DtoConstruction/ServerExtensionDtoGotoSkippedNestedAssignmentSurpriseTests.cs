using DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

namespace DotBoxD.Kernels.Tests.Plugins.Rpc;

public sealed class ServerExtensionDtoGotoSkippedNestedAssignmentSurpriseTests
{
    [Fact]
    public void Server_extension_rejects_dto_constructor_with_goto_skipped_nested_assignment()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class Profile
            {
                public Profile(string name)
                {
                    if (name.Length == 0)
                    {
                        goto Done;
                    }

                    {
                        Name = name;
                    }

                Done:
                    ;
                }

                public string Name { get; } = "lost";
            }

            [ServerExtension("profile-goto-skipped-assignment")]
            public sealed partial class ProfileKernel
            {
                public Profile Read(HookContext ctx) => new("");
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("Profile", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("constructor", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Server_extension_rejects_dto_constructor_with_goto_skipped_tuple_assignment()
    {
        var result = PluginAnalyzerGeneratedPackageFactory.RunGenerator("""
            using DotBoxD.Abstractions;
            using DotBoxD.Kernels;
            using DotBoxD.Kernels.Sandbox;
            using DotBoxD.Plugins;

            namespace Sample;

            public sealed class Profile
            {
                public Profile(string name, string city)
                {
                    if (name.Length == 0)
                    {
                        goto Done;
                    }

                    (Name, City) = (name, city);

                Done:
                    ;
                }

                public string Name { get; } = "lost";
                public string City { get; } = "lost";
            }

            [ServerExtension("profile-goto-skipped-tuple-assignment")]
            public sealed partial class ProfileKernel
            {
                public Profile Read(HookContext ctx) => new("", "");
            }
            """);

        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.Id == "DBXK100" &&
                diagnostic.GetMessage().Contains("Profile", StringComparison.Ordinal) &&
                diagnostic.GetMessage().Contains("constructor", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedTrees);
    }
}
