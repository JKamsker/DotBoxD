namespace DotBoxD.Services.Generated;

public enum RpcContractChangeKind
{
    Added,
    Removed,
    SignatureChanged,
    UnsupportedVersion
}

public sealed record RpcContractChange(
    RpcContractChangeKind Kind,
    string Contract,
    string? PreviousSignature,
    string? CurrentSignature)
{
    private RpcContractChangeKind _kind = ValidateAndReturn(Kind, Contract, PreviousSignature, CurrentSignature);
    private string _contract = Contract;
    private string? _previousSignature = PreviousSignature;
    private string? _currentSignature = CurrentSignature;

    public RpcContractChangeKind Kind
    {
        get => _kind;
        init
        {
            Validate(value, Contract, PreviousSignature, CurrentSignature);
            _kind = value;
        }
    }

    public string Contract
    {
        get => _contract;
        init
        {
            Validate(Kind, value, PreviousSignature, CurrentSignature);
            _contract = value;
        }
    }

    public string? PreviousSignature
    {
        get => _previousSignature;
        init
        {
            Validate(Kind, Contract, value, CurrentSignature);
            _previousSignature = value;
        }
    }

    public string? CurrentSignature
    {
        get => _currentSignature;
        init
        {
            Validate(Kind, Contract, PreviousSignature, value);
            _currentSignature = value;
        }
    }

    public bool IsBreaking => Kind is RpcContractChangeKind.Removed or RpcContractChangeKind.SignatureChanged or RpcContractChangeKind.UnsupportedVersion;

    private static void Validate(
        RpcContractChangeKind kind,
        string contract,
        string? previousSignature,
        string? currentSignature)
    {
        if (!IsKnownKind(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), kind, "Unknown RPC contract change kind.");
        }

        if (string.IsNullOrWhiteSpace(contract))
        {
            throw new ArgumentException("RPC contract change rows require a contract name.", nameof(Contract));
        }

        switch (kind)
        {
            case RpcContractChangeKind.Added:
                Require(currentSignature, nameof(CurrentSignature));
                break;
            case RpcContractChangeKind.Removed:
                Require(previousSignature, nameof(PreviousSignature));
                break;
            case RpcContractChangeKind.SignatureChanged:
            case RpcContractChangeKind.UnsupportedVersion:
                Require(previousSignature, nameof(PreviousSignature));
                Require(currentSignature, nameof(CurrentSignature));
                break;
        }
    }

    private static void Require(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("RPC contract change rows require the signature for this change kind.", paramName);
        }
    }

    private static bool IsKnownKind(RpcContractChangeKind kind)
        => kind is RpcContractChangeKind.Added
            or RpcContractChangeKind.Removed
            or RpcContractChangeKind.SignatureChanged
            or RpcContractChangeKind.UnsupportedVersion;

    private static RpcContractChangeKind ValidateAndReturn(
        RpcContractChangeKind kind,
        string contract,
        string? previousSignature,
        string? currentSignature)
    {
        Validate(kind, contract, previousSignature, currentSignature);
        return kind;
    }
}
