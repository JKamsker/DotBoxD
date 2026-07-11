using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Audit;

public sealed class BindingAuditFieldsContractTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_rejects_null_resource_kind()
    {
        var ex = Assert.Throws<ArgumentNullException>(
            () => BindingAuditFields.Create(null!, StartedAt, deterministic: true));

        Assert.Equal("resourceKind", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_resource_kind(string resourceKind)
    {
        var ex = Assert.Throws<ArgumentException>(
            () => BindingAuditFields.Create(resourceKind, StartedAt, deterministic: true));

        Assert.Equal("resourceKind", ex.ParamName);
    }

    [Theory]
    [InlineData("moduleHash")]
    [InlineData("policyHash")]
    public void Create_with_hashes_rejects_null_hashes(string parameterName)
    {
        var ex = Assert.Throws<ArgumentNullException>(() => CreateWithNullHash(parameterName));

        Assert.Equal(parameterName, ex.ParamName);
    }

    [Theory]
    [InlineData("bytesRead")]
    [InlineData("bytesWritten")]
    public void Create_rejects_negative_byte_counters(string parameterName)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => CreateWithNegativeBytes(parameterName));

        Assert.Equal(parameterName, ex.ParamName);
    }

    private static IReadOnlyDictionary<string, string> CreateWithNullHash(string parameterName)
        => parameterName switch
        {
            "moduleHash" => BindingAuditFields.Create(
                "file",
                StartedAt,
                moduleHash: null!,
                policyHash: "policy",
                deterministic: true),
            "policyHash" => BindingAuditFields.Create(
                "file",
                StartedAt,
                moduleHash: "module",
                policyHash: null!,
                deterministic: true),
            _ => UnexpectedParameterName(parameterName)
        };

    private static IReadOnlyDictionary<string, string> CreateWithNegativeBytes(string parameterName)
        => parameterName switch
        {
            "bytesRead" => BindingAuditFields.Create(
                "file",
                StartedAt,
                deterministic: true,
                bytesRead: -1),
            "bytesWritten" => BindingAuditFields.Create(
                "file",
                StartedAt,
                deterministic: true,
                bytesWritten: -1),
            _ => UnexpectedParameterName(parameterName)
        };

    private static IReadOnlyDictionary<string, string> UnexpectedParameterName(string parameterName)
        => throw new ArgumentOutOfRangeException(nameof(parameterName), parameterName, null);
}
