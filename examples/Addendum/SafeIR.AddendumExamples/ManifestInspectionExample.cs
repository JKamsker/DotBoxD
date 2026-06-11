namespace SafeIR.AddendumExamples;

internal static class ManifestInspectionExample
{
    public static void Run()
    {
        var package = FireDamagePluginPackage.Create();

        Console.WriteLine($"manifest: plugin={package.Manifest.PluginId}");
        foreach (var setting in package.Manifest.LiveSettings) {
            Console.WriteLine($"  setting {setting.Name}: {setting.Type} = {setting.DefaultValue}");
        }

        foreach (var effect in package.Manifest.Effects) {
            Console.WriteLine($"  effect {effect}");
        }

        foreach (var subscription in package.Manifest.Subscriptions) {
            Console.WriteLine($"  subscription {subscription.Event} -> {subscription.Kernel}");
        }
    }
}
