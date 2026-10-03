using System.Text;
using DotBoxD.Plugins.Replay;

namespace DotBoxD.Cli.Infrastructure;

internal static class InputFile
{
    public static async Task<string> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Export the generated package/IR as JSON; this tool never loads plugin assemblies.");
        }
        await using var stream = File.OpenRead(path);
        if (stream.Length > ExecutionTrace.MaximumJsonLength)
        {
            throw new InvalidDataException("Input exceeds the 16 MiB limit.");
        }
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true));
        var buffer = new char[8192];
        var result = new StringBuilder();
        int count;
        while ((count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (result.Length + count > ExecutionTrace.MaximumJsonLength)
            {
                throw new InvalidDataException("Input exceeds the size limit.");
            }
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
