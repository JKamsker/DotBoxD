using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class VersionHelperNullContractTests
{
    [Fact]
    public void TryParse_returns_false_for_null_text()
    {
        var parsed = SemVersion.TryParse(null!, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void Parse_rejects_null_text_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => SemVersion.Parse(null!));

        Assert.Equal("text", ex.ParamName);
    }

    [Fact]
    public void Supports_rejects_null_target_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => SandboxLanguage.Supports(null!));

        Assert.Equal("target", ex.ParamName);
    }
}
