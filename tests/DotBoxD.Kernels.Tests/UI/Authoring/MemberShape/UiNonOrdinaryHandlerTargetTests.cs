using Microsoft.CodeAnalysis;
using static DotBoxD.Kernels.Tests.UI.Authoring.UiGeneratorFixture;

namespace DotBoxD.Kernels.Tests.UI.Authoring;

public sealed class UiNonOrdinaryHandlerTargetTests
{
    public static TheoryData<string> UnsupportedTargets => new()
    {
        """
        using DotBoxD.UI.Authoring;

        public partial class Handlers
        {
            public static int Value
            {
                [UiRemoteHandler(7)] get => 1;
            }
        }
        """,
        """
        using DotBoxD.UI.Authoring;

        public partial class Handlers
        {
            [UiRemoteHandler(7)]
            public static Handlers operator +(Handlers left, Handlers right) => left;
        }
        """,
        """
        using DotBoxD.UI.Authoring;

        public partial class Handlers
        {
            [UiRemoteHandler(7)]
            public static implicit operator int(Handlers value) => 1;
        }
        """,
        """
        using DotBoxD.UI.Authoring;

        public static partial class Handlers
        {
            public static int Invoke()
            {
                [UiRemoteHandler(7)] int Local(int value) => value;
                return Local(1);
            }
        }
        """
    };

    [Theory]
    [MemberData(nameof(UnsupportedTargets))]
    public void Non_ordinary_handler_targets_report_a_focused_diagnostic(string source)
    {
        var result = Generate(source);

        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Result.GeneratedTrees);
    }
}
