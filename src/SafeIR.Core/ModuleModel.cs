namespace SafeIR;

public sealed record CapabilityRequest(string Id, string? Reason);

public sealed record SandboxModule(
    string Id,
    SemVersion Version,
    SemVersion TargetSandboxVersion,
    IReadOnlyList<CapabilityRequest> CapabilityRequests,
    IReadOnlyList<SandboxFunction> Functions,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SandboxFunction(
    string Id,
    bool IsEntrypoint,
    IReadOnlyList<Parameter> Parameters,
    SandboxType ReturnType,
    IReadOnlyList<Statement> Body,
    SandboxEffect? DeclaredEffects = null);

public sealed record Parameter(string Name, SandboxType Type);

public abstract record Statement(SourceSpan Span);

public sealed record AssignmentStatement(string Name, Expression Value, SourceSpan Span) : Statement(Span);

public sealed record ReturnStatement(Expression Value, SourceSpan Span) : Statement(Span);

public sealed record ExpressionStatement(Expression Value, SourceSpan Span) : Statement(Span);

public sealed record IfStatement(Expression Condition, IReadOnlyList<Statement> Then, IReadOnlyList<Statement> Else, SourceSpan Span)
    : Statement(Span);

public sealed record WhileStatement(Expression Condition, IReadOnlyList<Statement> Body, SourceSpan Span) : Statement(Span);

public sealed record ForRangeStatement(string LocalName, Expression Start, Expression End, IReadOnlyList<Statement> Body, SourceSpan Span)
    : Statement(Span);

public abstract record Expression(SourceSpan Span);

public sealed record LiteralExpression(SandboxValue Value, SourceSpan Span) : Expression(Span);

public sealed record VariableExpression(string Name, SourceSpan Span) : Expression(Span);

public sealed record UnaryExpression(string Operator, Expression Operand, SourceSpan Span) : Expression(Span);

public sealed record BinaryExpression(Expression Left, string Operator, Expression Right, SourceSpan Span) : Expression(Span);

public sealed record CallExpression(string Name, IReadOnlyList<Expression> Arguments, SandboxType? GenericType, SourceSpan Span)
    : Expression(Span);
