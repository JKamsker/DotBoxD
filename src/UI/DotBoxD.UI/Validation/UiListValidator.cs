
namespace DotBoxD.UI;

internal static class UiListValidator
{
    public static int Validate(UiValue value, UiPolicy policy)
    {
        if (value.Items.IsDefault || (value.Kind != UiValueKind.Items && !value.Items.IsEmpty) ||
            value.Items.Length > policy.MaxItems)
        {
            throw new UiValidationException("Invalid or over-limit UI item collection.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        foreach (var item in value.Items)
        {
            if (item is null || string.IsNullOrEmpty(item.Key) || !keys.Add(item.Key))
            { throw new UiValidationException("UI list keys must be nonempty and unique."); }
            bytes += UiValueValidator.Validate(UiValue.FromString(item.Key), policy);
            bytes += UiValueValidator.Validate(UiValue.FromString(item.Text), policy);
            if (bytes > policy.MaxStateBytes)
            { throw new UiValidationException("UI list exceeds the host state byte budget."); }
        }

        return checked((int)bytes);
    }
}
