namespace SafeIR.AddendumExamples;

using SafeIR.PluginIpc.Server.Abstractions;
using SafeIR.Plugins;

[GamePlugin("epic-items-only")]
public sealed partial class EpicItemsOnly : IItemFilter
{
    public bool Accept(ItemView item, PlayerView player)
        => item.Rarity >= Rarity.Epic;
}

public sealed class ArmorAdjustedDamageFormula : IDamageFormula
{
    public int Calculate(DamageInput input)
        => Math.Max(0, input.BaseDamage - input.Armor);
}

internal static class SimpleContractExamples
{
    public static void Run()
    {
        IItemFilter filter = new EpicItemsOnly();
        IDamageFormula formula = new ArmorAdjustedDamageFormula();

        var accepted = filter.Accept(
            new ItemView("ember-crown", Rarity.Epic),
            new PlayerView("player-1", 35));
        var damage = formula.Calculate(new DamageInput(120, 25));

        Console.WriteLine($"simple filter: accepted={accepted}");
        Console.WriteLine($"damage formula: finalDamage={damage}");
    }
}
