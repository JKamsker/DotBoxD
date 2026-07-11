using System.Globalization;

namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering.Expressions;

internal static partial class KernelMethodDescriptorPayloadParser
{
    private static readonly string[] RootPropertyNames =
    [
        "allocates",
        "capabilities",
        "contextType",
        "effects",
        "methodMetadataName",
        "normalizedSignature",
        "parameters",
        "returnType",
        "source",
        "version"
    ];

    private static readonly string[] ParameterPropertyNames = ["placeholder", "type"];

    public static bool TryParse(string payload, out KernelMethodDescriptorPayload? descriptor)
    {
        descriptor = null;
        if (!TryObject(payload, RootPropertyNames, out var properties))
        {
            return false;
        }

        if (!TryDescriptorScalars(properties, out var scalars))
        {
            return false;
        }

        if (!TryDescriptorCollections(properties, out var collections))
        {
            return false;
        }

        descriptor = new KernelMethodDescriptorPayload(
            scalars.Version,
            scalars.ContextType,
            scalars.MethodMetadataName,
            scalars.NormalizedSignature,
            scalars.ReturnType,
            scalars.Allocates,
            new EquatableArray<string>(collections.Capabilities),
            new EquatableArray<string>(collections.Effects),
            new EquatableArray<KernelMethodDescriptorParameter>(collections.Parameters),
            scalars.Source);
        return true;
    }

    private static bool TryObject(
        string json,
        IReadOnlyList<string> allowedNames,
        out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = SkipWhitespace(json, 0);
        if (!TryReadObjectStart(json, ref index))
        {
            return false;
        }

        while (true)
        {
            index = SkipWhitespace(json, index);
            if (TryFinishObject(json, index, out index))
            {
                return SkipWhitespace(json, index) == json.Length;
            }

            if (!TryReadObjectProperty(json, allowedNames, properties, ref index) ||
                !TryReadObjectSeparator(json, ref index))
            {
                return false;
            }
        }
    }

    private static bool TryReadObjectStart(string json, ref int index)
    {
        if (index >= json.Length || json[index] != '{')
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool TryFinishObject(string json, int index, out int nextIndex)
    {
        nextIndex = index;
        if (index >= json.Length || json[index] != '}')
        {
            return false;
        }

        nextIndex++;
        return true;
    }

    private static bool TryReadObjectProperty(
        string json,
        IReadOnlyList<string> allowedNames,
        Dictionary<string, string> properties,
        ref int index)
    {
        if (!TryReadString(json, ref index, out var name) ||
            !ContainsName(allowedNames, name) ||
            properties.ContainsKey(name))
        {
            return false;
        }

        index = SkipWhitespace(json, index);
        if (index >= json.Length || json[index++] != ':')
        {
            return false;
        }

        index = SkipWhitespace(json, index);
        var valueStart = index;
        if (!SkipValue(json, ref index))
        {
            return false;
        }

        properties.Add(name, json.Substring(valueStart, index - valueStart));
        return true;
    }

    private static bool TryReadObjectSeparator(string json, ref int index)
    {
        index = SkipWhitespace(json, index);
        if (index < json.Length && json[index] == ',')
        {
            index++;
            return true;
        }

        return index < json.Length && json[index] == '}';
    }

    private static bool TryParameters(
        Dictionary<string, string> properties,
        out KernelMethodDescriptorParameter[] parameters)
    {
        parameters = [];
        if (!properties.TryGetValue("parameters", out var raw) ||
            !TryArray(raw, out var items))
        {
            return false;
        }

        var result = new KernelMethodDescriptorParameter[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (!TryObject(items[i], ParameterPropertyNames, out var item) ||
                !TryString(item, "placeholder", out var placeholder) ||
                !TryString(item, "type", out var type))
            {
                return false;
            }

            result[i] = new KernelMethodDescriptorParameter(placeholder, type);
        }

        parameters = result;
        return true;
    }

    private static bool TryString(Dictionary<string, string> properties, string name, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(name, out var raw))
        {
            return false;
        }

        var index = 0;
        return TryReadString(raw, ref index, out value) && SkipWhitespace(raw, index) == raw.Length;
    }

    private static bool TryStringArray(Dictionary<string, string> properties, string name, out string[] values)
    {
        values = [];
        if (!properties.TryGetValue(name, out var raw) ||
            !TryArray(raw, out var items))
        {
            return false;
        }

        values = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var index = 0;
            if (!TryReadString(items[i], ref index, out values[i]) ||
                SkipWhitespace(items[i], index) != items[i].Length)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryBool(Dictionary<string, string> properties, string name, out bool value)
    {
        value = false;
        if (!properties.TryGetValue(name, out var raw))
        {
            return false;
        }

        if (string.Equals(raw, "true", StringComparison.Ordinal))
        {
            value = true;
            return true;
        }

        return string.Equals(raw, "false", StringComparison.Ordinal);
    }

    private static bool TryInt(Dictionary<string, string> properties, string name, out int value)
    {
        value = 0;
        return properties.TryGetValue(name, out var raw) &&
               int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryArray(string json, out List<string> items)
    {
        items = [];
        var index = SkipWhitespace(json, 0);
        if (index >= json.Length || json[index++] != '[')
        {
            return false;
        }

        while (true)
        {
            index = SkipWhitespace(json, index);
            if (index < json.Length && json[index] == ']')
            {
                index++;
                return SkipWhitespace(json, index) == json.Length;
            }

            var valueStart = index;
            if (!SkipValue(json, ref index))
            {
                return false;
            }

            items.Add(json.Substring(valueStart, index - valueStart));
            if (TryReadArraySeparator(json, ref index))
            {
                continue;
            }

            return false;
        }
    }

    private static bool TryReadArraySeparator(string json, ref int index)
    {
        index = SkipWhitespace(json, index);
        if (index >= json.Length)
        {
            return false;
        }

        if (json[index] == ',')
        {
            index++;
            return true;
        }

        return json[index] == ']';
    }

    private static bool ContainsName(IReadOnlyList<string> allowedNames, string name)
    {
        for (var i = 0; i < allowedNames.Count; i++)
        {
            if (string.Equals(allowedNames[i], name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

}
