using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace ShaRPC.SourceGenerator;

internal static class IdentifierHelpers
{
    public static string EscapeIdentifier(string name)
    {
        var kind = SyntaxFacts.GetKeywordKind(name);
        return kind == SyntaxKind.None ? name : "@" + name;
    }

    public static string EscapeNamespace(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
        {
            return namespaceName;
        }

        return string.Join(".", namespaceName.Split('.').Select(EscapeIdentifier));
    }

    public static string QualifyTypeName(string @namespace, string typeName)
    {
        var escapedTypeName = EscapeIdentifier(typeName);
        if (string.IsNullOrEmpty(@namespace))
        {
            return "global::" + escapedTypeName;
        }

        return "global::" + EscapeNamespace(@namespace) + "." + escapedTypeName;
    }
}
