namespace CodeEnforcer;

internal static class RepositoryPaths
{
    private static readonly string[] ConfigPathParts =
    [
        ".config",
        "code-enforcer",
        "code-enforcer.json"
    ];

    public static string DiscoverRoot(string startDirectory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            string gitPath = Path.Combine(current.FullName, ".git");
            if (File.Exists(gitPath) || Directory.Exists(gitPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new CodeEnforcerException("Could not find repository root.", ExitCodes.InputError);
    }

    public static string DiscoverConfigPath(string startDirectory)
    {
        DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
        while (current is not null)
        {
            string configPath = Path.Combine([current.FullName, .. ConfigPathParts]);
            if (File.Exists(configPath))
            {
                return configPath;
            }

            current = current.Parent;
        }

        throw new CodeEnforcerException(
            "Could not find .config/code-enforcer/code-enforcer.json in the current directory or its parents.",
            ExitCodes.InputError);
    }
}
