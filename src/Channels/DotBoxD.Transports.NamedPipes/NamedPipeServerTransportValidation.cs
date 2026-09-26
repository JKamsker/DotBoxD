using System.IO.Pipes;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Transports.NamedPipes;

internal static class NamedPipeServerTransportValidation
{
    private const int MaxSpecificServerInstances = 254;

    internal static string ValidatePipeName(string pipeName)
    {
        if (pipeName is null)
        {
            throw new ArgumentNullException(nameof(pipeName));
        }

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            throw new ArgumentException("Pipe name cannot be null, empty, or whitespace.", nameof(pipeName));
        }

        return pipeName;
    }

    internal static int ValidateMaxAllowedServerInstances(int value, string paramName)
    {
        if (value == NamedPipeServerStream.MaxAllowedServerInstances)
        {
            return value;
        }

        if (value < 1 || value > MaxSpecificServerInstances)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Maximum server instances must be {NamedPipeServerStream.MaxAllowedServerInstances} or between 1 and {MaxSpecificServerInstances}.");
        }

        return value;
    }

    internal static int ValidateMaxMessageSize(int value, string paramName)
    {
        if (value < MessageFramer.HeaderSize || value > MessageFramer.MaxMessageSize)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Maximum message size must be between {MessageFramer.HeaderSize} and {MessageFramer.MaxMessageSize} bytes.");
        }

        return value;
    }
}
