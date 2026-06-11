namespace SafeIR.PluginIpc.Server.Abstractions;

public interface DamageSettings
{
    bool Enabled { get; set; }
    string DamageType { get; set; }
    int MinDamage { get; set; }
}

public sealed record MyEvent(int Value, string TargetId);
