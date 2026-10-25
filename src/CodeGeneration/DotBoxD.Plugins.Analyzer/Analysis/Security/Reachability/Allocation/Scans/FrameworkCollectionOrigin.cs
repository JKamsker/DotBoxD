using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace DotBoxD.Plugins.Analyzer.Analysis;

internal static class FrameworkCollectionOrigin
{
    public static bool IsExact(IOperation source, Compilation compilation)
        => IsExact(source, compilation, new HashSet<SyntaxNode>());

    private static bool IsExact(IOperation? source, Compilation compilation, HashSet<SyntaxNode> visited)
    {
        while (source is IConversionOperation { OperatorMethod: null } conversion)
        { source = conversion.Operand; }
        if (source is null || !visited.Add(source.Syntax))
        { return false; }
        if (HasExactRuntimeType(source.Type))
        { return true; }
        return source switch
        {
            IObjectCreationOperation { Type: INamedTypeSymbol created } => FrameworkCollectionIdentity.IsFrameworkType(created),
            IFieldReferenceOperation field => IsExactField(field.Field, compilation, visited),
            ILocalReferenceOperation local => IsExactLocal(local, compilation, visited),
            IPropertyReferenceOperation property => IsFrameworkView(property),
            _ => false
        };
    }

    private static bool HasExactRuntimeType(ITypeSymbol? type)
        => type is IArrayTypeSymbol || type is INamedTypeSymbol { IsSealed: true } sealedType &&
            FrameworkCollectionIdentity.IsFrameworkType(sealedType);

    private static bool IsExactField(IFieldSymbol field, Compilation compilation, HashSet<SyntaxNode> visited)
    {
        if (!field.IsReadOnly || HasInitializationWrite(field, compilation))
        { return false; }
        var initializer = field.DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .OfType<VariableDeclaratorSyntax>().Select(declaration => declaration.Initializer?.Value).SingleOrDefault();
        if (initializer is null)
        { return false; }
        var origin = compilation.GetSemanticModel(initializer.SyntaxTree).GetOperation(initializer);
        return SymbolEqualityComparer.Default.Equals(origin?.Type, field.Type) && IsExact(origin, compilation, visited);
    }

    private static bool HasInitializationWrite(IFieldSymbol field, Compilation compilation)
    {
        foreach (var constructor in InitializationMethods(field.ContainingType))
        {
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                if (syntax.DescendantNodes().Any(node => IsWriteOrReference(node, field, model)))
                { return true; }
            }
        }
        return false;
    }

    private static IEnumerable<IMethodSymbol> InitializationMethods(INamedTypeSymbol type)
        => type.GetMembers().OfType<IMethodSymbol>().Where(method =>
            method.MethodKind is MethodKind.Constructor or MethodKind.StaticConstructor)
            .Concat(type.GetMembers().OfType<IPropertySymbol>().Select(property => property.SetMethod)
                .OfType<IMethodSymbol>().Where(setter => setter.IsInitOnly));

    private static bool IsWriteOrReference(SyntaxNode node, ISymbol symbol, SemanticModel model)
    {
        var target = node switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left,
            ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) => argument.Expression,
            RefExpressionSyntax reference => reference.Expression,
            ForEachVariableStatementSyntax loop => loop.Variable,
            _ => null
        };
        return target is not null && IsStorageReference(model.GetOperation(target), symbol);
    }

    private static bool IsStorageReference(IOperation? operation, ISymbol symbol)
        => operation switch
        {
            IFieldReferenceOperation field => SymbolEqualityComparer.Default.Equals(field.Field, symbol),
            ILocalReferenceOperation local => SymbolEqualityComparer.Default.Equals(local.Local, symbol),
            ITupleOperation tuple => tuple.Elements.Any(element => IsStorageReference(element, symbol)),
            IConversionOperation conversion => IsStorageReference(conversion.Operand, symbol),
            _ => false
        };

    private static bool IsExactLocal(ILocalReferenceOperation local, Compilation compilation, HashSet<SyntaxNode> visited)
    {
        var declaration = local.Local.DeclaringSyntaxReferences.SingleOrDefault()?.GetSyntax();
        if (declaration?.FirstAncestorOrSelf<BlockSyntax>() is not { } block ||
            local.Syntax.Ancestors().Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
        { return false; }
        var model = compilation.GetSemanticModel(block.SyntaxTree);
        if (block.DescendantNodes().Any(node => node is GotoStatementSyntax || IsWriteOrReferenceEscape(node, local.Local, model)))
        { return false; }
        var values = LocalValueOrigins.GetValues(local, compilation).ToArray();
        return values.Length > 0 && values.All(value =>
            IsExact(compilation.GetSemanticModel(value.SyntaxTree).GetOperation(value), compilation, new HashSet<SyntaxNode>(visited)));
    }

    private static bool IsWriteOrReferenceEscape(SyntaxNode node, ILocalSymbol local, SemanticModel model)
        => IsUntrackedLocalWrite(node) && IsWriteOrReference(node, local, model) ||
           node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax &&
           node.DescendantNodes().OfType<IdentifierNameSyntax>().Any(name =>
               SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, local));

    private static bool IsUntrackedLocalWrite(SyntaxNode node)
        => node is ArgumentSyntax or RefExpressionSyntax or ForEachVariableStatementSyntax ||
           node is AssignmentExpressionSyntax assignment &&
           (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || assignment.Left is TupleExpressionSyntax);

    private static bool IsFrameworkView(IPropertyReferenceOperation property)
        => property.Property.Name is "Keys" or "Values" &&
           property.Property.GetMethod is { IsVirtual: false } &&
           FrameworkCollectionIdentity.IsFrameworkType(property.Property.ContainingType) &&
           property.Property.ContainingType.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) is
               "System.Collections.Generic.Dictionary<TKey, TValue>" or
               "System.Collections.Generic.SortedDictionary<TKey, TValue>" or
               "System.Collections.Generic.SortedList<TKey, TValue>";
}
