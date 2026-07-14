namespace DotBoxD.DebugAdapter.Diagnostics;

internal static class AdapterDiagnostics
{
    private static readonly object Gate = new();
    private static string? _logPath;

    public static void Configure(string logPath)
    {
        lock (Gate)
        {
            _logPath = logPath;
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
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
}
