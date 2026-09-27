using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveSettingExactRangeTests
{
    private const long Large = 9_007_199_254_740_992L;

    [Theory]
    [InlineData("unsigned-min")]
    [InlineData("decimal-min")]
    [InlineData("decimal-max")]
    [InlineData("fractional-min")]
    [InlineData("fractional-max")]
    [InlineData("negative-fractional-max")]
    public void Default_outside_exact_range_is_rejected(string scenario)
    {
        var definition = scenario switch
        {
            "unsigned-min" => new LiveSettingDefinition("Limit", "long", long.MaxValue, Min: (ulong)long.MaxValue + 1),
            "decimal-min" => new LiveSettingDefinition("Limit", "long", Large, Min: Large + 1m),
            "decimal-max" => new LiveSettingDefinition("Limit", "long", Large + 1, Max: (decimal)Large),
            "fractional-min" => new LiveSettingDefinition("Limit", "long", Large, Min: Large + 0.5m),
            "fractional-max" => new LiveSettingDefinition("Limit", "long", Large + 1, Max: Large + 0.5m),
            "negative-fractional-max" => new LiveSettingDefinition("Limit", "long", -Large, Max: -Large - 0.5m),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var error = Assert.Throws<SandboxValidationException>(() => LiveSettingStore.FromDefinitions([definition]));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK023");
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("decimal")]
    [InlineData("fractional")]
    public void Inverted_exact_range_is_rejected(string scenario)
    {
        var definition = scenario switch
        {
            "unsigned" => new LiveSettingDefinition("Limit", "long", long.MaxValue, (ulong)long.MaxValue + 1, long.MaxValue),
            "decimal" => new LiveSettingDefinition("Limit", "long", Large, Large + 1m, Large),
            "fractional" => new LiveSettingDefinition("Limit", "long", Large, Large + 0.5m, Large),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var error = Assert.Throws<SandboxValidationException>(() => LiveSettingStore.FromDefinitions([definition]));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK024");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Rejected_update_preserves_the_previous_value(bool minimum, bool fractional)
    {
        var bound = fractional ? Large + 0.5m : minimum ? Large + 1m : Large;
        var initial = minimum ? Large + 1 : Large;
        var definition = new LiveSettingDefinition("Limit", "long", initial,
            Min: minimum ? bound : null, Max: minimum ? null : bound);
        var store = LiveSettingStore.FromDefinitions([definition]);
        var outside = minimum ? Large : Large + 1;

        var error = Assert.Throws<SandboxValidationException>(() => store.Set("Limit", outside));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK023");
        Assert.Equal(initial, store.Get<long>("Limit"));
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("decimal")]
    [InlineData("fractional")]
    [InlineData("floating")]
    public void Valid_boundaries_keep_their_numeric_domain(string scenario)
    {
        var definition = scenario switch
        {
            "unsigned" => new LiveSettingDefinition("Limit", "long", long.MaxValue, (ulong)long.MaxValue, ulong.MaxValue),
            "decimal" => new LiveSettingDefinition("Limit", "long", Large + 1, Large + 1m, Large + 1m),
            "fractional" => new LiveSettingDefinition("Limit", "long", Large + 1, Large + 0.5m, Large + 1.5m),
            "floating" => new LiveSettingDefinition("Limit", "double", (double)Large, (double)Large, (double)Large),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var store = LiveSettingStore.FromDefinitions([definition]);

        Assert.Equal(definition.DefaultValue, store.GetObject("Limit"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Install_rejects_a_default_outside_a_fractional_decimal_bound(bool minimum)
    {
        using var server = PluginAddendumTestPolicies.CreateServer();
        var package = FireDamagePluginPackage.Create();
        package = package with
        {
            Manifest = package.Manifest with
            {
                LiveSettings = package.Manifest.LiveSettings.Select(setting => setting.Name == "MinDamage"
                    ? setting with
                    {
                        Min = minimum ? 100.0000000000000000000000001m : null,
                        Max = minimum ? null : 99.99999999999999999999999999m
                    }
                    : setting).ToArray()
            }
        };

        var error = await Assert.ThrowsAsync<SandboxValidationException>(
            async () => await server.InstallAsync(package).AsTask());

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK023");
    }
}
