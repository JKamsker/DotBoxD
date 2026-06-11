namespace SafeIR.PluginIpc.Server.Abstractions;

public interface IItemFilter
{
    bool Accept(ItemView item, PlayerView player);
}

public sealed record ItemView(string Id, Rarity Rarity);

public sealed record PlayerView(string Id, int Level);

public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary
}
