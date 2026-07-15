using DotBoxD.Plugins.Analyzer.Analysis.Lowering;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class ForbiddenApiNamePolicy
{
    private static readonly string[] ExactTypeNames =
    [
        DotBoxDGenerationNames.TypeNames.SystemActivator, DotBoxDGenerationNames.TypeNames.SystemConsole,
        DotBoxDGenerationNames.TypeNames.SystemAppContext, DotBoxDGenerationNames.TypeNames.SystemAppDomain,
        DotBoxDGenerationNames.TypeNames.SystemEnvironment, DotBoxDGenerationNames.TypeNames.SystemGc,
        DotBoxDGenerationNames.TypeNames.SystemGcSettings, DotBoxDGenerationNames.TypeNames.SystemTimeZoneInfo,
        DotBoxDGenerationNames.TypeNames.SystemDelegate, DotBoxDGenerationNames.TypeNames.SystemServiceProvider,
        DotBoxDGenerationNames.TypeNames.SystemType, DotBoxDGenerationNames.TypeNames.SystemUnsafe,
        DotBoxDGenerationNames.TypeNames.SystemComponentModelTypeDescriptor,
        "System.Buffers.MemoryPool<T>",
        "Microsoft.Win32.Registry", "System.Security.Principal.WindowsIdentity",
        "System.Security.Cryptography.X509Certificates.X509Store"
    ];

    private static readonly string[] NamespacePrefixes =
    [
        "System.IO.", "System.Net.", "System.Reflection.", "System.Runtime.InteropServices.",
        "System.Runtime.Loader.", "System.Diagnostics.", "System.Threading.", "System.Threading.Tasks.",
        "System.Linq.Expressions.", "System.Security.Principal.", "System.Data.", "Microsoft.CSharp.",
        "Microsoft.EntityFrameworkCore.", "Microsoft.Win32."
    ];

    public static bool IsForbiddenExactType(string name) => Array.IndexOf(ExactTypeNames, name) >= 0;

    public static bool IsForbiddenNamespace(string name)
        => NamespacePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
