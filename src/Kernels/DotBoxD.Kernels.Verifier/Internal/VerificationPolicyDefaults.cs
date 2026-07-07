using DotBoxD.Kernels.Sandbox;
using static DotBoxD.Kernels.Verifier.VerifierTypeNames;

namespace DotBoxD.Kernels.Verifier;

internal static class VerificationPolicyDefaults
{
    public static string RuntimeMember(string name, string parameters, string returnType)
        => $"{CompiledRuntimeName}.{name}({parameters}):{returnType}";

    public static string DotBoxDAssemblyVersion()
        => typeof(SandboxValue).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public static string AssemblyIdentity(string name, string version, string culture, string publicKeyToken)
        => $"{name}, Version={version}, Culture={culture}, PublicKeyToken={publicKeyToken}";

    public static IReadOnlySet<string> RuntimeFacadeIdentityDefaults()
        => new HashSet<string>(StringComparer.Ordinal) {
            AssemblyModuleIdentity(typeof(SandboxValue).Assembly),
            AssemblyModuleIdentity(typeof(Runtime.CompiledRuntime).Assembly)
        };

    private static string AssemblyModuleIdentity(System.Reflection.Assembly assembly)
    {
        var name = assembly.GetName();
        return $"{name.Name}, Version={name.Version}, Mvid={assembly.ManifestModule.ModuleVersionId:N}";
    }
}
