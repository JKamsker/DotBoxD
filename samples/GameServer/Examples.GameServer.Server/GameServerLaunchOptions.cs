namespace DotBoxD.Kernels.Game.Server;

internal sealed record GameServerLaunchOptions(bool UseBuilder, string? ExternalPluginPipeName)
{
    public const string Usage =
        "Usage: Examples.GameServer.Server [--use-builder] [--external-plugin --pipe-name <named-pipe-name>]";

    public bool LaunchPlugin => ExternalPluginPipeName is null;

    public static bool TryParse(string[] args, out GameServerLaunchOptions options, out string error)
    {
        var useBuilder = false;
        var externalPlugin = false;
        string? pipeName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--use-builder":
                    useBuilder = true;
                    break;

                case "--external-plugin":
                    externalPlugin = true;
                    break;

                case "--pipe-name":
                    if (++i == args.Length || string.IsNullOrWhiteSpace(args[i]))
                    {
                        return Fail(out options, out error, "--pipe-name requires a non-empty value.");
                    }

                    pipeName = args[i];
                    break;

                default:
                    return Fail(out options, out error, $"Unknown argument: {args[i]}");
            }
        }

        if (!externalPlugin && pipeName is not null)
        {
            return Fail(out options, out error, "--pipe-name requires --external-plugin.");
        }

        if (externalPlugin && pipeName is null)
        {
            return Fail(out options, out error, "--external-plugin requires --pipe-name.");
        }

        if (externalPlugin && useBuilder)
        {
            return Fail(out options, out error, "--use-builder only applies when the server launches the plugin.");
        }

        options = new GameServerLaunchOptions(useBuilder, pipeName);
        error = string.Empty;
        return true;
    }

    private static bool Fail(out GameServerLaunchOptions options, out string error, string message)
    {
        options = new GameServerLaunchOptions(false, null);
        error = message;
        return false;
    }
}
