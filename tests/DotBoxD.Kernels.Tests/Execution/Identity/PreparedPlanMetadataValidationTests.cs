using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Tests.Execution;

public sealed class PreparedPlanMetadataValidationTests
{
    public static IEnumerable<object[]> MetadataCases()
    {
        foreach (var field in new[] { "ModuleHash", "PlanHash", "PolicyHash", "BindingManifestHash", "Budget" })
        {
            yield return [field, false];
            yield return [field, true];
        }
    }

    [Theory]
    [MemberData(nameof(MetadataCases))]
    public async Task Cached_and_rebuilt_validation_require_consistent_metadata(string field, bool warmCache)
    {
        var fixture = await PreparedPlanIdentityFixture.CreateAsync();
        var candidate = fixture.Copy(changedField: field);
        var error = Assert.Throws<SandboxValidationException>(() => fixture.Validate(candidate, warmCache));
        Assert.Contains(error.Diagnostics, diagnostic => diagnostic.Code == "E-PLAN-INTEGRITY");
    }

    [Theory]
    [InlineData("Original", false)]
    [InlineData("Original", true)]
    [InlineData("Copy", false)]
    [InlineData("Copy", true)]
    [InlineData("EqualValues", false)]
    [InlineData("EqualValues", true)]
    public async Task Valid_original_and_copied_identities_are_accepted(string kind, bool warmCache)
    {
        var fixture = await PreparedPlanIdentityFixture.CreateAsync();
        var candidate = kind == "Original" ? fixture.Plan : fixture.Copy(copyValues: kind == "EqualValues");
        fixture.Validate(candidate, warmCache);
    }
}
