namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Runtime;

/// <summary>
/// P5 fail-safe coverage: a projected DTO whose field is derived in the constructor body (not a constructor
/// parameter) cannot be expressed as <c>record.new</c> — every persisted field must be a passed argument. Rather
/// than silently drop the derived field from the generated projection, the chain fails safe: it is skipped and no
/// projection IR is emitted, so generation stays valid. Shares the <see cref="RemoteRunLocalChainRuntimeTests"/>
/// harness.
/// </summary>
public sealed partial class RemoteRunLocalChainRuntimeTests
{
    private const string DerivedFieldSource = Prelude + """
        public sealed class DerivedInfo
        {
            public string Zone { get; }
            public int ZoneLength { get; }     // derived in the constructor, NOT a constructor parameter
            public DerivedInfo(string zone)
            {
                Zone = zone;
                ZoneLength = zone.Length;
            }
        }

        public static class DerivedFieldUsage
        {
            public static void Configure(RemoteHookRegistry hooks)
                => hooks.On<Ev.EncounterEvent>().Where(e => e.Distance <= 4)
                    .Select(e => new DerivedInfo(e.Zone))
                    .RunLocal((info, ctx) => { });
        }
        """;

    [Fact]
    public void Dto_with_a_constructor_derived_field_fails_safe_instead_of_dropping_it()
    {
        // DerivedInfo.ZoneLength is set only in the ctor body, so it is not one of record.new's arguments. The chain
        // is skipped (not lowered) rather than emitting a 1-field record that silently omits ZoneLength — and the
        // generated code stays valid. Compile asserts emit success internally.
        _ = Compile(DerivedFieldSource, enableInterceptors: true);
        Assert.DoesNotContain("record.new", GeneratedSource(DerivedFieldSource));
    }
}
