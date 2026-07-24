namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Core;

internal static class GenericConstructionWorklistSources
{
    public const string SelfCycle = """
        using DotBoxD.Abstractions;

        public static class GenericFactory
        {
            public static T Create<T>(bool recurse) where T : new()
                => recurse ? Create<T>(false) : new T();
        }

        public sealed class DangerousConstructed
        {
            public DangerousConstructed() => _ = System.IO.File.ReadAllText("/x");
        }

        [Plugin("generic-self-cycle")]
        public sealed class GenericKernel : IEventKernel<string>
        {
            public bool ShouldHandle(string e, HookContext context)
            {
                _ = GenericFactory.Create<DangerousConstructed>(true);
                return true;
            }

            public void Handle(string e, HookContext context) { }
        }
        """;

    public const string Diamond = """
        using DotBoxD.Abstractions;

        public static class GenericFactory
        {
            public static void Create<T>() where T : new()
            {
                Left<T>();
                Right<T>();
            }

            private static void Left<T>() where T : new() => Construct<T>();
            private static void Right<T>() where T : new() => Construct<T>();
            private static void Construct<T>() where T : new() => _ = new T();
        }

        public sealed class DangerousConstructed
        {
            public DangerousConstructed() => _ = System.IO.File.ReadAllText("/x");
        }

        [Plugin("generic-diamond")]
        public sealed class GenericKernel : IEventKernel<string>
        {
            public bool ShouldHandle(string e, HookContext context)
            {
                GenericFactory.Create<DangerousConstructed>();
                return true;
            }

            public void Handle(string e, HookContext context) { }
        }
        """;

    public const string TwoSlotPermutation = """
        using DotBoxD.Abstractions;

        public static class GenericFactory
        {
            public static void Create<TSafe, TDangerous>()
                where TSafe : new()
                where TDangerous : new()
                => Swap<TDangerous, TSafe>();

            private static void Swap<TConstructed, TIgnored>()
                where TConstructed : new()
                where TIgnored : new()
                => _ = new TConstructed();
        }

        public sealed class SafeConstructed { }

        public sealed class DangerousConstructed
        {
            public DangerousConstructed() => _ = System.IO.File.ReadAllText("/x");
        }

        [Plugin("generic-two-slot-permutation")]
        public sealed class GenericKernel : IEventKernel<string>
        {
            public bool ShouldHandle(string e, HookContext context)
            {
                GenericFactory.Create<SafeConstructed, DangerousConstructed>();
                return true;
            }

            public void Handle(string e, HookContext context) { }
        }
        """;

    public const string ContainingTypeCycle = """
        using DotBoxD.Abstractions;

        public sealed class GenericFactory<T> where T : new()
        {
            public void Create(bool recurse)
            {
                if (recurse)
                {
                    Create(false);
                    return;
                }

                _ = new T();
            }
        }

        public sealed class DangerousConstructed
        {
            public DangerousConstructed() => _ = System.IO.File.ReadAllText("/x");
        }

        [Plugin("generic-containing-type-cycle")]
        public sealed class GenericKernel : IEventKernel<string>
        {
            public bool ShouldHandle(string e, HookContext context)
            {
                new GenericFactory<DangerousConstructed>().Create(true);
                return true;
            }

            public void Handle(string e, HookContext context) { }
        }
        """;
}
