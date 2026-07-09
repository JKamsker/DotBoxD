namespace DotBoxD.Plugins.Runtime;

/// <summary>
/// Compares the event-type name a package manifest subscribes to (<c>HookSubscriptionManifest.Event</c> /
/// the <c>IEventKernel&lt;TEvent&gt;</c> contract payload) against the name a runtime event adapter or a
/// <c>typeof(TEvent)</c> reports.
/// </summary>
/// <remarks>
/// The analyzer now emits the <b>fully-qualified</b> name (<c>Namespace.TypeName</c>) into generated
/// manifests so two contract types that share a simple name stay distinct. Existing manifests produced
/// before that change — and hand-written <see cref="IPluginEventAdapter{TEvent}"/> implementations that
/// return only <c>typeof(TEvent).Name</c> — still carry the unqualified simple name. To stay backward
/// compatible while keeping the new disambiguation, two names match when they are ordinally equal OR when
/// one is a namespace-qualified form whose final segment equals the other's simple name. Comparing the
/// simple-name tail is unambiguous in practice because the producer always pairs a manifest's contract and
/// subscription event from the same symbol, so only the qualified-vs-simple seam between manifest and
/// adapter is bridged here, never two unrelated qualified names.
/// </remarks>
internal static class EventNameMatch
{
    public static string CanonicalTypeName(Type type)
    {
        if (type.IsArray)
        {
            return CanonicalTypeName(type.GetElementType()!) + "[]";
        }

        var name = UnqualifiedTypeName(type);
        if (type.DeclaringType is not null)
        {
            name = CanonicalTypeName(type.DeclaringType) + "." + name;
        }
        else if (!string.IsNullOrEmpty(type.Namespace) && !IsSystemNamespace(type.Namespace))
        {
            name = type.Namespace + "." + name;
        }

        return type.IsGenericType
            ? name + "<" + string.Join(", ", type.GetGenericArguments().Select(CanonicalTypeName)) + ">"
            : name;
    }

    public static bool Matches(string? left, string? right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return false;
        }

        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            return true;
        }

        var leftSimpleName = SimpleName(normalizedLeft);
        var rightSimpleName = SimpleName(normalizedRight);
        if (!string.Equals(leftSimpleName, rightSimpleName, StringComparison.Ordinal))
        {
            return false;
        }

        var leftQualified = IsQualified(normalizedLeft);
        var rightQualified = IsQualified(normalizedRight);
        return leftQualified != rightQualified || IsClosedGenericName(leftSimpleName);
    }

    private static string Normalize(string name)
    {
        const string globalPrefix = "global::";
        var normalized = name.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? name[globalPrefix.Length..]
            : name;

        return NormalizeSystemTypeName(NormalizeReflectionGenericName(normalized));
    }

    private static string NormalizeReflectionGenericName(string name)
    {
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick < 0)
        {
            return name;
        }

        var argumentsStart = name.IndexOf('[', tick);
        if (argumentsStart < 0)
        {
            return name;
        }

        var arguments = ReflectionGenericArguments(name, argumentsStart);
        return arguments.Count == 0
            ? name
            : NormalizeSystemTypeName(name[..tick]) + "<" + string.Join(", ", arguments.Select(NormalizeReflectionArgument)) + ">";
    }

    private static List<string> ReflectionGenericArguments(string name, int argumentsStart)
    {
        if (argumentsStart >= name.Length - 1 || name[argumentsStart] != '[' || name[^1] != ']')
        {
            return [];
        }

        var inner = name[(argumentsStart + 1)..^1];
        var arguments = new List<string>();
        for (var i = 0; i < inner.Length;)
        {
            if (inner[i] != '[')
            {
                var end = TopLevelComma(inner, i);
                arguments.Add(inner[i..end]);
                i = end < inner.Length ? end + 1 : end;
                continue;
            }

            var start = ++i;
            var depth = 0;
            while (i < inner.Length)
            {
                if (inner[i] == '[')
                {
                    depth++;
                }
                else if (inner[i] == ']')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                i++;
            }

            if (i >= inner.Length)
            {
                return [];
            }

            arguments.Add(inner[start..i]);
            i++;
            if (i < inner.Length && inner[i] == ',')
            {
                i++;
            }
        }

        return arguments;
    }

    private static string NormalizeReflectionArgument(string argument)
    {
        var end = TopLevelComma(argument, 0);
        return Normalize(argument[..end]);
    }

    private static string NormalizeSystemTypeName(string name)
        => name.StartsWith("System.", StringComparison.Ordinal)
            ? SimpleName(name)
            : name;

    private static string UnqualifiedTypeName(Type type)
    {
        var tick = type.Name.IndexOf('`');
        return tick < 0 ? type.Name : type.Name[..tick];
    }

    private static bool IsSystemNamespace(string @namespace)
        => string.Equals(@namespace, "System", StringComparison.Ordinal) ||
           @namespace.StartsWith("System.", StringComparison.Ordinal);

    private static int TopLevelComma(string value, int start)
    {
        var bracketDepth = 0;
        for (var i = start; i < value.Length; i++)
        {
            if (value[i] == '[')
            {
                bracketDepth++;
            }
            else if (value[i] == ']')
            {
                bracketDepth--;
            }
            else if (value[i] == ',' && bracketDepth == 0)
            {
                return i;
            }
        }

        return value.Length;
    }

    private static bool IsQualified(string name) => TopLevelLastDot(name) >= 0;

    private static bool IsClosedGenericName(string name) => name.Contains('<', StringComparison.Ordinal);

    private static string SimpleName(string name)
    {
        var lastDot = TopLevelLastDot(name);
        return lastDot >= 0 && lastDot < name.Length - 1
            ? name[(lastDot + 1)..]
            : name;
    }

    private static int TopLevelLastDot(string name)
    {
        var angleDepth = 0;
        var bracketDepth = 0;
        for (var i = name.Length - 1; i >= 0; i--)
        {
            if (name[i] == '>')
            {
                angleDepth++;
            }
            else if (name[i] == '<')
            {
                angleDepth--;
            }
            else if (name[i] == ']')
            {
                bracketDepth++;
            }
            else if (name[i] == '[')
            {
                bracketDepth--;
            }
            else if (name[i] == '.' && angleDepth == 0 && bracketDepth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
