using System.Collections.Immutable;

namespace DotBoxD.UI;

/// <summary>Checks evaluated properties before controls are created or a state batch is committed.</summary>
public static class UiMaterializationValidator
{
    public static void Validate(int staticNodeCount, ImmutableArray<UiPropertyValue> values, UiPolicy policy)
    {
        long count = staticNodeCount;
        foreach (var property in values)
        {
            UiValueValidator.Validate(property.Value, policy);
            if (property.PropertyId == UiPropertyId.Items)
            {
                if (property.Value.Items.Length > policy.MaxChildren)
                { throw new UiValidationException("Keyed UI rows exceed the host child limit."); }
                count += property.Value.Items.Length;
            }
            RequireLayoutRange(property);
        }

        ValidateRanges(values);
        if (count > policy.MaxNodes)
        { throw new UiValidationException("UI materialization exceeds the host node limit."); }
    }

    private static void ValidateRanges(ImmutableArray<UiPropertyValue> values)
    {
        var maxima = values.Where(p => p.PropertyId == UiPropertyId.Maximum).ToDictionary(p => p.NodeId, p => p.Value.Number);
        foreach (var property in values.Where(p => p.PropertyId == UiPropertyId.Value))
        {
            var maximum = maxima.GetValueOrDefault(property.NodeId, 100);
            if (property.Value.Number > maximum)
            { throw new UiValidationException("UI value exceeds its control maximum."); }
        }
    }

    private static bool PositiveRange(double value) => value > 0 && value <= 1_000_000_000;
    private static bool InRange(double value, double minimum, double maximum) => value >= minimum && value <= maximum;

    private static void RequireLayoutRange(UiPropertyValue property)
    {
        var value = property.Value;
        var valid = property.PropertyId switch
        {
            UiPropertyId.Columns => InRange(value.Integer, 1, 64),
            UiPropertyId.Row or UiPropertyId.Column => InRange(value.Integer, 0, 63),
            UiPropertyId.Padding or UiPropertyId.Spacing => InRange(value.Number, 0, 1024),
            UiPropertyId.Maximum => PositiveRange(value.Number),
            UiPropertyId.Value => InRange(value.Number, 0, 1_000_000_000),
            _ => true
        };
        if (!valid)
        { throw new UiValidationException("UI layout/value property is outside its supported range."); }
    }
}
