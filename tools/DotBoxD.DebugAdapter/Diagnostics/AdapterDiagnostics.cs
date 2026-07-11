namespace DotBoxD.DebugAdapter.Diagnostics;

internal static class AdapterDiagnostics
{
    private static string? _logPath;

    public static void Configure(string logPath) => _logPath = logPath;

    public static void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(_logPath))
        {
            return;
        }

        try
        {
            File.AppendAllText(_logPath, $"{DateTime.UtcNow:O} adapter {message}{Environment.NewLine}");
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
        }
    }
}
