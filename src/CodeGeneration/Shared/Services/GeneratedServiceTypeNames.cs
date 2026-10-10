using System.Globalization;
using System.Text;

namespace DotBoxD.CodeGeneration.Services;

internal static class GeneratedServiceTypeNames
{
    public const string Namespace = "DotBoxD.Services.Generated";
    public const string LegacyExtensions = "DotBoxDGeneratedExtensions";

    // Escape underscores as well as punctuation so different assembly names cannot collapse to
    // the same identifier (for example, Contracts.A and Contracts_002eA).
    public static string Extensions(string assemblyName)
    {
        var builder = new StringBuilder(LegacyExtensions + "_");
        foreach (var character in assemblyName)
        {
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('_').Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
