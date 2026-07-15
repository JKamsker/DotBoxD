using System.Text.Json;

namespace DotBoxD.Pushdown.Services;

internal static class PluginDebugBridgeDiscovery
{
    public static PluginDebugBridgeDescriptor Publish(PluginDebugBridgeDescriptor descriptor)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotBoxD",
            "Debug");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(
            directory,
            descriptor.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json");
        var published = descriptor with { DiscoveryFile = path };
        var content = JsonSerializer.SerializeToUtf8Bytes(published);
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough
        };
        var unixMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = unixMode;
        }

        using var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, unixMode);
        }

        stream.Write(content);
        return published;
    }

    public static void Remove(PluginDebugBridgeDescriptor descriptor)
    {
        if (descriptor.DiscoveryFile is null)
        {
            return;
        }

        try
        {
            File.Delete(descriptor.DiscoveryFile);
        }
        catch (IOException)
        {
            // Stale discovery files contain an expired token and cannot attach to a disposed pipe.
        }
        catch (UnauthorizedAccessException)
        {
            // Same safe stale-file outcome.
        }
    }

    public static byte[]? ReadSource(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
