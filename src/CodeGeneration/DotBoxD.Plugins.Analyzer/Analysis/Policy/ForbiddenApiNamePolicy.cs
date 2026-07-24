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
        "System.Progress<T>",
        DotBoxDGenerationNames.TypeNames.SystemDelegate, DotBoxDGenerationNames.TypeNames.SystemServiceProvider,
        DotBoxDGenerationNames.TypeNames.SystemType, DotBoxDGenerationNames.TypeNames.SystemUnsafe,
        DotBoxDGenerationNames.TypeNames.SystemArrayPoolOriginal,
        DotBoxDGenerationNames.TypeNames.SystemComponentModelBackgroundWorker,
        DotBoxDGenerationNames.TypeNames.SystemComponentModelTypeDescriptor,
        "System.Text.StringBuilder",
        "System.ComponentModel.LicenseManager",
        "System.Collections.Concurrent.BlockingCollection<T>",
        "System.Buffers.MemoryPool<T>",
        "System.Formats.Tar.TarFile",
        "Microsoft.Win32.Registry", "System.Linq.ParallelEnumerable", "System.Numerics.BigInteger",
        "System.OperatingSystem",
        "System.Runtime.ProfileOptimization",
        "System.Security.Principal.WindowsIdentity", "System.UriParser",
        "System.Security.Cryptography.X509Certificates.X509Store",
        "System.Security.Cryptography.CngKey",
        "System.Security.Cryptography.CspKeyContainerInfo",
        "System.Security.Cryptography.CspParameters",
        "System.Security.Cryptography.DSACryptoServiceProvider",
        "System.Security.Cryptography.RSACryptoServiceProvider",
        "System.Security.Cryptography.Rfc2898DeriveBytes",
        "System.Security.Cryptography.CryptoConfig",
        "System.Security.AccessControl.DirectorySecurity",
        "System.Security.AccessControl.FileSecurity",
        "Microsoft.Extensions.FileProviders.PhysicalFileProvider",
        "Microsoft.Extensions.Localization.ResourceManagerStringLocalizerFactory",
        "Microsoft.Extensions.Logging.ConsoleLoggerExtensions",
        "Microsoft.Extensions.Configuration.FileConfigurationExtensions",
        "Microsoft.Extensions.Configuration.IniConfigurationExtensions",
        "Microsoft.Extensions.Configuration.JsonConfigurationExtensions",
        "Microsoft.Extensions.Configuration.XmlConfigurationExtensions",
        "Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions"
    ];

    private static readonly string[] NamespacePrefixes =
    [
        "System.IO.", "System.Net.", "System.Reflection.", "System.Resources.", "System.Runtime.InteropServices.",
        "System.Runtime.Intrinsics.", "System.Runtime.Loader.", "System.Diagnostics.", "System.Threading.",
        "System.Threading.Tasks.",
        "System.Timers.", "System.Linq.Expressions.", "System.Security.Principal.", "System.Transactions.",
        "System.Data.", "Microsoft.AspNetCore.", "Microsoft.CSharp.", "Microsoft.VisualBasic.",
        "Microsoft.EntityFrameworkCore.", "Microsoft.Extensions.Logging.Console.",
        "Microsoft.Extensions.Logging.EventLog.",
        "Microsoft.Extensions.Configuration.UserSecrets.",
        "Microsoft.Win32."
    ];

    private static readonly string[] ExactMemberNames =
    [
        "Microsoft.Extensions.Configuration.EnvironmentVariablesExtensions.AddEnvironmentVariables",
        "Microsoft.Extensions.Logging.EventLoggerFactoryExtensions.AddEventLog",
        "Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder",
        "System.String.Create",
        "System.Security.Cryptography.RSA.Create",
        "Microsoft.Extensions.Configuration.KeyPerFileConfigurationBuilderExtensions.AddKeyPerFile"
    ];

    public static bool IsForbiddenExactType(string name) => Array.IndexOf(ExactTypeNames, name) >= 0;

    public static bool IsForbiddenNamespace(string name)
        => NamespacePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    public static bool IsForbiddenExactMember(string name) => Array.IndexOf(ExactMemberNames, name) >= 0;
}
