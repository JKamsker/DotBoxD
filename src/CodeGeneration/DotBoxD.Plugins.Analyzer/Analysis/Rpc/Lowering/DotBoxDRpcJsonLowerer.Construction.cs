using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal sealed partial class DotBoxDRpcJsonLowerer
{
    public DotBoxDRpcJsonLowerer(
        SemanticModel model,
        ICollection<string> capabilities,
        ICollection<string> effects,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, RpcInlinedBinding>? inlinedBindings = null,
        IReadOnlyCollection<string>? inlineStack = null,
        List<string>? expressionPrelude = null,
        Func<string, string>? reserveGeneratedName = null,
        string? serverContextParameterName = null,
        ITypeSymbol? serverContextType = null)
    {
        _model = model;
        _capabilities = capabilities;
        _effects = effects;
        _cancellationToken = cancellationToken;
        _inlinedBindings = inlinedBindings;
        _inlineStack = inlineStack;
        _expressionPrelude = expressionPrelude;
        _reserveGeneratedName = reserveGeneratedName;
        _serverContextParameterName = serverContextParameterName;
        _serverContextType = serverContextType;
        _serverContextHostBindings = new ServerContextHostBindingResolver(
            model,
            serverContextParameterName,
            serverContextType,
            cancellationToken);
    }

    private string LowerRecordCreation(BaseObjectCreationExpressionSyntax creation)
    {
        var created = TypeOf(creation);
        if (TryLowerEmptyListCreation(creation, created) is { } emptyList)
        {
            return emptyList;
        }

        if (TryLowerMapCreation(creation, created) is { } emptyMap)
        {
            return emptyMap;
        }

        var named = RequireRecordDto(creation, created);
        Allocates = true;
        var fields = DotBoxDRpcTypeMapper.RecordFields(named);
        var args = LowerRecordCreationArguments(creation, named, fields);
        return Call("record.new", DotBoxDRpcTypeMapper.JsonType(named, _model.Compilation), args);
    }

    private string? TryLowerEmptyListCreation(
        BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol created)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(created) is not { } elementType ||
            creation.Initializer is not null ||
            creation.ArgumentList is { Arguments.Count: > 0 })
        {
            return null;
        }

        Allocates = true;
        return Call("list.empty", DotBoxDRpcTypeMapper.JsonType(elementType, _model.Compilation));
    }

    private static INamedTypeSymbol RequireRecordDto(
        BaseObjectCreationExpressionSyntax creation,
        ITypeSymbol created)
    {
        if (created is INamedTypeSymbol named && DotBoxDRpcTypeMapper.IsRecordDto(named))
        {
            return named;
        }

        throw new NotSupportedException(
            $"Server extension '{CreationText(creation)}' must construct a supported DTO or empty list.");
    }

    private string[] LowerRecordCreationArguments(
        BaseObjectCreationExpressionSyntax creation,
        INamedTypeSymbol named,
        IReadOnlyList<RecordMember> fields)
    {
        var args = new string[fields.Count];
        if (TryBindConstructorCreation(creation, named, fields, args))
        {
            return args;
        }

        if (TryBindInitializerCreation(creation, named, fields, args))
        {
            return args;
        }

        throw new NotSupportedException($"Server extension 'new {named.Name}' must use constructor arguments or an object initializer.");
    }

    private bool TryBindConstructorCreation(
        BaseObjectCreationExpressionSyntax creation,
        INamedTypeSymbol named,
        IReadOnlyList<RecordMember> fields,
        string[] args)
    {
        if (creation.ArgumentList is not { Arguments.Count: > 0 } argumentList)
        {
            return false;
        }

        var constructor = ConstructorForCreation(creation, named, fields);
        var lowered = LowerArgumentsInParameterOrder(
            argumentList.Arguments,
            constructor.Parameters,
            $"Server extension constructor for '{named.Name}'");
        var assigned = new bool[fields.Count];
        BindConstructorArguments(constructor, fields, named, lowered, args, assigned);
        if (creation.Initializer is { } initializer)
        {
            BindInitializer(initializer, fields, named, args, assigned, requireAllFields: false);
        }

        FillDerivedFields(fields, named, args, assigned);
        return true;
    }

    private IMethodSymbol ConstructorForCreation(
        BaseObjectCreationExpressionSyntax creation,
        INamedTypeSymbol named,
        IReadOnlyList<RecordMember> fields)
    {
        if (_model.GetSymbolInfo(creation, _cancellationToken).Symbol is IMethodSymbol constructor &&
            constructor.Parameters.Length <= fields.Count)
        {
            return constructor;
        }

        throw new NotSupportedException($"Server extension constructor for '{named.Name}' must map each constructor parameter to a record field.");
    }

    private static void BindConstructorArguments(
        IMethodSymbol constructor,
        IReadOnlyList<RecordMember> fields,
        INamedTypeSymbol named,
        IReadOnlyList<string> lowered,
        string[] args,
        bool[] assigned)
    {
        for (var i = 0; i < constructor.Parameters.Length; i++)
        {
            BindConstructorArgument(constructor.Parameters[i], fields, named, lowered[i], args, assigned);
        }
    }

    private static void BindConstructorArgument(
        IParameterSymbol parameter,
        IReadOnlyList<RecordMember> fields,
        INamedTypeSymbol named,
        string lowered,
        string[] args,
        bool[] assigned)
    {
        var fieldIndex = ConstructorFieldIndex(fields, parameter, named);
        if (assigned[fieldIndex])
        {
            throw new NotSupportedException(
                $"Server extension constructor for '{named.Name}' must map one argument per field.");
        }

        args[fieldIndex] = lowered;
        assigned[fieldIndex] = true;
    }

    private bool TryBindInitializerCreation(
        BaseObjectCreationExpressionSyntax creation,
        INamedTypeSymbol named,
        IReadOnlyList<RecordMember> fields,
        string[] args)
    {
        if (creation.Initializer is not { } initializer)
        {
            return false;
        }

        BindInitializer(initializer, fields, named, args, assigned: null, requireAllFields: true);
        return true;
    }

    private string[] FillDerivedFields(
        IReadOnlyList<RecordMember> fields,
        INamedTypeSymbol named,
        string[] args,
        bool[] assigned)
    {
        while (TryLowerDerivedField(fields, assigned, args, named))
        {
        }

        for (var i = 0; i < fields.Count; i++)
        {
            if (!assigned[i])
            {
                args[i] = LowerDerivedField(fields, assigned, args, named, fields[i]);
            }
        }

        return args;
    }

    private static string CreationText(BaseObjectCreationExpressionSyntax creation)
        => creation is ObjectCreationExpressionSyntax explicitCreation
            ? "new " + explicitCreation.Type
            : creation.ToString();

    private void BindInitializer(
        InitializerExpressionSyntax initializer,
        IReadOnlyList<RecordMember> fields,
        INamedTypeSymbol named,
        string[] args,
        bool[]? assigned,
        bool requireAllFields)
    {
        assigned ??= new bool[fields.Count];
        foreach (var entry in initializer.Expressions)
        {
            if (entry is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax fieldName } assignment)
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must assign named fields.");
            }
            var index = IndexOfField(fields, fieldName.Identifier.ValueText, named);
            args[index] = HoistInitializerMember(LowerExpression(assignment.Right));
            assigned[index] = true;
        }
        if (!requireAllFields)
        {
            return;
        }

        while (TryLowerDerivedField(fields, assigned, args, named))
        {
        }

        for (var i = 0; i < assigned.Length; i++)
        {
            if (!assigned[i])
            {
                throw new NotSupportedException($"Server extension initializer for '{named.Name}' must set field '{fields[i].Name}'.");
            }
        }
    }

    private string HoistInitializerMember(string value)
    {
        if (_expressionPrelude is null)
        {
            return value;
        }

        var localName = ReserveGeneratedLocal("__sir_arg");
        AddExpressionPrelude(SetStatement(localName, value));
        return Var(localName);
    }

    private static int IndexOfField(IReadOnlyList<RecordMember> fields, string name, INamedTypeSymbol named)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new NotSupportedException($"Server extension '{named.Name}' has no field '{name}'.");
    }

    private static int ConstructorFieldIndex(
        IReadOnlyList<RecordMember> fields,
        IParameterSymbol parameter,
        INamedTypeSymbol named)
    {
        var index = RpcDtoFieldMatcher.FieldIndex(fields, parameter);
        if (index >= 0)
        {
            return index;
        }

        throw new NotSupportedException(
            $"Server extension DTO '{named.Name}' must expose a constructor matching its public fields.");
    }
}
