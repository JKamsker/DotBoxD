using System.Text;

namespace DotBoxD.UI;

public static class UiValueValidator
{
    public static int Validate(UiValue value, UiPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (value is null || !Enum.IsDefined(value.Kind) || value.Text is null || !double.IsFinite(value.Number))
        {
            throw new UiValidationException("Unsupported or malformed UI scalar value.");
        }

        RequireUnusedDefaults(value);

        if (value.Text.Length > policy.MaxStringLength)
        {
            throw new UiValidationException("UI string exceeds the host limit.");
        }

        RequireWellFormedText(value.Text);
        var listBytes = UiListValidator.Validate(value, policy);
        return listBytes + (value.Kind == UiValueKind.String ? Encoding.UTF8.GetByteCount(value.Text) + 8 : 8);
    }

    private static void RequireUnusedDefaults(UiValue value)
    {
        if ((value.Kind != UiValueKind.Boolean && value.Boolean) ||
            (value.Kind != UiValueKind.Int32 && value.Integer != 0) ||
            (value.Kind != UiValueKind.Number && value.Number != 0) ||
            (value.Kind != UiValueKind.String && value.Text.Length != 0))
        {
            throw new UiValidationException("Unused UI scalar fields must retain their defaults.");
        }
    }

    private static void RequireWellFormedText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var character = text[i];
            if (char.IsHighSurrogate(character))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i]))
                {
                    throw new UiValidationException("UI strings must contain well-formed UTF-16.");
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                throw new UiValidationException("UI strings must contain well-formed UTF-16.");
            }
        }

    }

    public static UiValueKind PropertyKind(UiPrimitive primitive, UiPropertyId property)
        => property switch
        {
            UiPropertyId.Enabled => UiValueKind.Boolean,
            UiPropertyId.Visible => UiValueKind.Boolean,
            UiPropertyId.Text => TextKind(primitive),
            UiPropertyId.Checked => RequirePrimitive(primitive, UiPrimitive.CheckBox, UiValueKind.Boolean),
            UiPropertyId.Value => RangeKind(primitive),
            UiPropertyId.Maximum => RangeKind(primitive),
            UiPropertyId.Items => RequirePrimitive(primitive, UiPrimitive.Items, UiValueKind.Items),
            _ => LayoutKind(primitive, property)
        };

    private static UiValueKind LayoutKind(UiPrimitive primitive, UiPropertyId property) => property switch
    {
        UiPropertyId.Horizontal => RequirePrimitive(primitive, UiPrimitive.Stack, UiValueKind.Boolean),
        UiPropertyId.Spacing => RequirePrimitive(primitive, UiPrimitive.Stack, UiValueKind.Number),
        UiPropertyId.Columns => RequirePrimitive(primitive, UiPrimitive.Grid, UiValueKind.Int32),
        UiPropertyId.Row or UiPropertyId.Column => UiValueKind.Int32,
        UiPropertyId.Padding => RequirePrimitive(primitive, UiPrimitive.Border, UiValueKind.Number),
        _ => throw new UiValidationException("Unsupported property for UI primitive.")
    };

    private static UiValueKind RangeKind(UiPrimitive primitive)
        => primitive is UiPrimitive.ProgressBar or UiPrimitive.Slider
            ? UiValueKind.Number : throw new UiValidationException("Unsupported value property.");

    private static UiValueKind TextKind(UiPrimitive primitive)
        => primitive is UiPrimitive.Text or UiPrimitive.Button or UiPrimitive.TextBox or UiPrimitive.CheckBox
            ? UiValueKind.String : throw new UiValidationException("Unsupported text property for UI primitive.");

    private static UiValueKind RequirePrimitive(UiPrimitive primitive, UiPrimitive required, UiValueKind kind)
        => primitive == required ? kind : throw new UiValidationException("Unsupported property for UI primitive.");
}
