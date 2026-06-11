namespace SafeIR.PluginIpc.Server.Abstractions;

public interface IDamageFormula
{
    int Calculate(DamageInput input);
}

public sealed record DamageInput(int BaseDamage, int Armor);
