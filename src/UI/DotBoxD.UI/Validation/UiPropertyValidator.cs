namespace DotBoxD.UI;

internal static class UiPropertyValidator
{
    public static void Validate(UiProperty property, UiPrimitive primitive, HashSet<UiPropertyId> properties,
        Dictionary<int, UiStateSlot> state, Dictionary<int, UiKernel> kernels, UiPolicy policy)
    {
        RequireSource(property, properties);
        var kind = UiValueValidator.PropertyKind(primitive, property.Id);
        if (property.Literal is { } literal)
        {
            UiValueValidator.Validate(literal, policy);
            if (literal.Kind != kind)
            {
                throw new UiValidationException("UI literal property type mismatch.");
            }
        }
        else if (property.StateSlotId != 0)
        {
            if (!state.TryGetValue(property.StateSlotId, out var slot) || slot.InitialValue.Kind != kind)
            {
                throw new UiValidationException("Unknown or incompatible UI state binding.");
            }
        }
        else if (!kernels.ContainsKey(property.BindingKernelId))
        {
            throw new UiValidationException("Unknown UI binding kernel.");
        }

        RequireTwoWay(property, primitive);
    }

    private static void RequireSource(UiProperty property, HashSet<UiPropertyId> properties)
    {
        if (property is null || !properties.Add(property.Id) || property.StateSlotId < 0 || property.BindingKernelId < 0 ||
            (property.Literal is null ? 0 : 1) + (property.StateSlotId == 0 ? 0 : 1) +
            (property.BindingKernelId == 0 ? 0 : 1) != 1)
        {
            throw new UiValidationException("Duplicate or ambiguous UI property source.");
        }
    }

    private static void RequireTwoWay(UiProperty property, UiPrimitive primitive)
    {
        if (property.TwoWay && (property.StateSlotId == 0 ||
            !((primitive == UiPrimitive.TextBox && property.Id == UiPropertyId.Text) ||
              (primitive == UiPrimitive.CheckBox && property.Id == UiPropertyId.Checked))))
        {
            throw new UiValidationException("Two-way bindings require a supported input property and state slot.");
        }
    }
}
