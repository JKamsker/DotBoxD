using DotBoxD.Kernels.Sandbox;
using DotBoxD.Plugins;
using DotBoxD.Plugins.Runtime;

namespace DotBoxD.Kernels.Tests.Plugins.LiveSettings;

public sealed class LiveSettingStoreReentrantRollbackSurpriseTests
{
    [Fact]
    public void Failed_batch_rolls_back_a_reentrant_store_update()
    {
        var side = new LiveValue<int>("Side", 1);
        LiveSettingStore? store = null;
        var first = new CallbackLiveSetting("First", 1, value =>
        {
            if (value is 10)
            {
                store!.Set("Side", 99);
            }
        });
        var second = new ThrowingLiveSetting("Second", 2, 20);
        store = new LiveSettingStore([side, first, second]);

        var exception = Assert.Throws<InvalidOperationException>(() => store.SetMany(
            new Dictionary<string, object?>
            {
                ["First"] = 10,
                ["Second"] = 20
            }));

        Assert.Equal("second failed", exception.Message);
        Assert.Equal(1, store.Get<int>("Side"));
        Assert.Equal(1, store.Get<int>("First"));
        Assert.Equal(2, store.Get<int>("Second"));
    }

    [Fact]
    public void Caught_nested_batch_failure_rolls_back_only_the_nested_batch()
    {
        var nestedFirst = new LiveValue<int>("NestedFirst", 1);
        var nestedSecond = new ThrowingLiveSetting("NestedSecond", 2, 20);
        LiveSettingStore? store = null;
        var outer = new CallbackLiveSetting("Outer", 1, value =>
        {
            if (value is 10)
            {
                Assert.Throws<InvalidOperationException>(() => store!.SetMany(
                    new Dictionary<string, object?>
                    {
                        ["NestedFirst"] = 10,
                        ["NestedSecond"] = 20
                    }));
            }
        });
        store = new LiveSettingStore([nestedFirst, nestedSecond, outer]);

        store.SetMany(new Dictionary<string, object?> { ["Outer"] = 10 });

        Assert.Equal(10, store.Get<int>("Outer"));
        Assert.Equal(1, store.Get<int>("NestedFirst"));
        Assert.Equal(2, store.Get<int>("NestedSecond"));
    }

    private sealed class CallbackLiveSetting(string name, int initialValue, Action<object?> callback) : ILiveSetting
    {
        private int _value = initialValue;

        public string Name => name;
        public LiveSettingDefinition Definition { get; } = new(name, "int", initialValue);
        public object? CurrentValue => _value;

        public SandboxValue ToSandboxValue() => SandboxValue.FromInt32(_value);

        public void SetObject(object? value)
        {
            _value = Assert.IsType<int>(value);
            callback(value);
        }
    }

    private sealed class ThrowingLiveSetting(string name, int initialValue, int rejectedValue) : ILiveSetting
    {
        private int _value = initialValue;

        public string Name => name;
        public LiveSettingDefinition Definition { get; } = new(name, "int", initialValue);
        public object? CurrentValue => _value;

        public SandboxValue ToSandboxValue() => SandboxValue.FromInt32(_value);

        public void SetObject(object? value)
        {
            var typedValue = Assert.IsType<int>(value);
            if (typedValue == rejectedValue)
            {
                throw new InvalidOperationException("second failed");
            }

            _value = typedValue;
        }
    }
}
