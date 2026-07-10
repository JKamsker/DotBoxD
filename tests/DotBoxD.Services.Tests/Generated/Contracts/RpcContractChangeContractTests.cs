using Xunit;

namespace DotBoxD.Services.Tests.Generated;

public sealed class RpcContractChangeContractTests
{
    private const string Contract = "game/get";
    private const string PreviousSignature = "old";
    private const string CurrentSignature = "new";

    [Theory]
    [InlineData("undefined-kind-constructor", "Kind")]
    [InlineData("undefined-kind-init", "Kind")]
    [InlineData("null-contract-constructor", "Contract")]
    [InlineData("blank-contract-constructor", "Contract")]
    [InlineData("blank-contract-init", "Contract")]
    [InlineData("added-missing-current-constructor", "CurrentSignature")]
    [InlineData("added-missing-current-init", "CurrentSignature")]
    [InlineData("removed-missing-previous-constructor", "PreviousSignature")]
    [InlineData("removed-missing-previous-init", "PreviousSignature")]
    [InlineData("signature-changed-missing-previous-constructor", "PreviousSignature")]
    [InlineData("signature-changed-missing-current-constructor", "CurrentSignature")]
    [InlineData("unsupported-version-missing-previous-constructor", "PreviousSignature")]
    [InlineData("unsupported-version-missing-current-constructor", "CurrentSignature")]
    public void Constructor_and_init_reject_malformed_change_rows(string caseName, string expectedParamName)
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => CreateInvalidChange(caseName));

        Assert.Equal(expectedParamName, ex.ParamName);
    }

    [Theory]
    [InlineData(RpcContractChangeKind.Added, null, CurrentSignature, false)]
    [InlineData(RpcContractChangeKind.Removed, PreviousSignature, null, true)]
    [InlineData(RpcContractChangeKind.SignatureChanged, PreviousSignature, CurrentSignature, true)]
    [InlineData(RpcContractChangeKind.UnsupportedVersion, "0", "1", true)]
    public void Constructor_accepts_valid_change_rows(
        RpcContractChangeKind kind,
        string? previousSignature,
        string? currentSignature,
        bool expectedBreaking)
    {
        var change = new RpcContractChange(kind, Contract, previousSignature, currentSignature);

        Assert.Equal(kind, change.Kind);
        Assert.Equal(Contract, change.Contract);
        Assert.Equal(previousSignature, change.PreviousSignature);
        Assert.Equal(currentSignature, change.CurrentSignature);
        Assert.Equal(expectedBreaking, change.IsBreaking);
    }

    private static RpcContractChange CreateInvalidChange(string caseName)
        => caseName switch
        {
            "undefined-kind-constructor" => new RpcContractChange((RpcContractChangeKind)99, Contract, null, CurrentSignature),
            "undefined-kind-init" => ValidAdded() with { Kind = (RpcContractChangeKind)99 },
            "null-contract-constructor" => new RpcContractChange(RpcContractChangeKind.Added, null!, null, CurrentSignature),
            "blank-contract-constructor" => new RpcContractChange(RpcContractChangeKind.Added, "   ", null, CurrentSignature),
            "blank-contract-init" => ValidAdded() with { Contract = "   " },
            "added-missing-current-constructor" => new RpcContractChange(RpcContractChangeKind.Added, Contract, null, null),
            "added-missing-current-init" => ValidAdded() with { CurrentSignature = null },
            "removed-missing-previous-constructor" => new RpcContractChange(RpcContractChangeKind.Removed, Contract, null, null),
            "removed-missing-previous-init" => ValidRemoved() with { PreviousSignature = null },
            "signature-changed-missing-previous-constructor" => new RpcContractChange(
                RpcContractChangeKind.SignatureChanged,
                Contract,
                null,
                CurrentSignature),
            "signature-changed-missing-current-constructor" => new RpcContractChange(
                RpcContractChangeKind.SignatureChanged,
                Contract,
                PreviousSignature,
                null),
            "unsupported-version-missing-previous-constructor" => new RpcContractChange(
                RpcContractChangeKind.UnsupportedVersion,
                "$manifest",
                null,
                "1"),
            "unsupported-version-missing-current-constructor" => new RpcContractChange(
                RpcContractChangeKind.UnsupportedVersion,
                "$manifest",
                "0",
                null),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown malformed change row.")
        };

    private static RpcContractChange ValidAdded()
        => new(RpcContractChangeKind.Added, Contract, null, CurrentSignature);

    private static RpcContractChange ValidRemoved()
        => new(RpcContractChangeKind.Removed, Contract, PreviousSignature, null);
}
