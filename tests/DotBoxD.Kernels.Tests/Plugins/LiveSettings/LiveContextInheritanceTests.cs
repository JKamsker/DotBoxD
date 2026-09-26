using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveContextInheritanceTests
{
    [Fact]
    public void BindContext_initializes_and_updates_inherited_properties()
    {
        using var server = PluginServer.Create();
        var context = server.BindContext<IDerivedSettings>("settings", settings =>
        {
            settings.Limit = 42;
            settings.Name = "ready";
        });

        Assert.Equal(42, context.Value.Limit);
        Assert.Equal("ready", context.Value.Name);
        context.Value.Limit = 43;
        Assert.Equal(43, context.Settings.Get<int>("Limit"));
        Assert.Equal(2, context.Settings.Definitions.Count);
    }

    [Fact]
    public void Store_view_maps_inherited_accessors_to_existing_slots()
    {
        var store = new LiveSettingStore([new LiveValue<int>("Limit", 42), new LiveValue<string>("Name", "ready")]);
        var settings = store.As<IDerivedSettings>();

        Assert.Equal(42, settings.Limit);
        Assert.Equal("ready", settings.Name);
        settings.Limit = 43;
        Assert.Equal(43, store.Get<int>("Limit"));
    }

    [Fact]
    public void Diamond_inheritance_creates_one_slot_for_the_shared_property()
    {
        using var server = PluginServer.Create();
        var context = server.BindContext<IDiamondSettings>("settings");

        ((ILeftSettings)context.Value).Limit = 42;

        Assert.Equal(42, ((IRightSettings)context.Value).Limit);
        Assert.Equal("Limit", Assert.Single(context.Settings.Definitions).Name);
    }

    [Fact]
    public void Equal_properties_from_distinct_interfaces_share_one_slot()
    {
        using var server = PluginServer.Create();
        var context = server.BindContext<ICombinedSettings>("settings");

        ((IBaseSettings)context.Value).Limit = 42;

        Assert.Equal(42, ((IAlternateSettings)context.Value).Limit);
        Assert.Single(context.Settings.Definitions);
    }

    [Fact]
    public void Redeclared_property_and_base_accessors_share_one_slot()
    {
        using var server = PluginServer.Create();
        var context = server.BindContext<IRedeclaredSettings>("settings");

        context.Value.Limit = 42;
        Assert.Equal(42, ((IBaseSettings)context.Value).Limit);
        ((IBaseSettings)context.Value).Limit = 43;
        Assert.Equal(43, context.Value.Limit);
        Assert.Single(context.Settings.Definitions);
    }

    [Fact]
    public void BindContext_rejects_conflicting_inherited_property_types()
    {
        using var server = PluginServer.Create();

        var error = Assert.Throws<SandboxValidationException>(() => server.BindContext<IConflictingSettings>("settings"));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Contains("Limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindContext_rejects_an_inherited_indexer_at_materialization()
    {
        using var server = PluginServer.Create();

        var error = Assert.Throws<SandboxValidationException>(() => server.BindContext<IInheritedIndexer>("settings"));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Contains("indexer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Store_view_rejects_an_inherited_indexer_at_materialization()
    {
        var store = new LiveSettingStore([new LiveValue<int>("Item", 1)]);

        var error = Assert.Throws<SandboxValidationException>(() => store.As<IInheritedIndexer>());

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
        Assert.Contains("indexer", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BindContext_rejects_an_unsupported_inherited_property_type()
    {
        using var server = PluginServer.Create();

        var error = Assert.Throws<SandboxValidationException>(() => server.BindContext<IInheritedUnsupported>("settings"));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
    }

    [Fact]
    public void BindContext_rejects_an_inherited_property_without_a_setter()
    {
        using var server = PluginServer.Create();

        var error = Assert.Throws<SandboxValidationException>(() => server.BindContext<IInheritedReadOnly>("settings"));

        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "DBXK020");
    }

    private interface IBaseSettings
    {
        int Limit { get; set; }
    }

    private interface IDerivedSettings : IBaseSettings
    {
        string Name { get; set; }
    }

    private interface ILeftSettings : IBaseSettings;
    private interface IRightSettings : IBaseSettings;
    private interface IDiamondSettings : ILeftSettings, IRightSettings;

    private interface IAlternateSettings
    {
        int Limit { get; set; }
    }

    private interface ICombinedSettings : IBaseSettings, IAlternateSettings;

    private interface IRedeclaredSettings : IBaseSettings
    {
        new int Limit { get; set; }
    }

    private interface IStringSettings
    {
        string Limit { get; set; }
    }

    private interface IConflictingSettings : IBaseSettings, IStringSettings;

    private interface IIndexer
    {
        int this[int index] { get; set; }
    }

    private interface IInheritedIndexer : IIndexer;

    private interface IUnsupported
    {
        DayOfWeek Day { get; set; }
    }

    private interface IInheritedUnsupported : IUnsupported;

    private interface IReadOnlySettings
    {
        int Limit { get; }
    }

    private interface IInheritedReadOnly : IReadOnlySettings;
}
