namespace DotBoxD.Services.Diagnostics;

internal static class DiagnosticArgumentGuard
{
    public static string RequireNonBlank(string value, string paramName, string label)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(label + " must not be empty or whitespace.", paramName);
        }

        return value;
    }
}
