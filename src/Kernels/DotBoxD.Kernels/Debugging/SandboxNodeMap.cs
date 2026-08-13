using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Debugging;

/// <summary>Builds versioned node IDs from function identity and IR tree position.</summary>
public sealed class SandboxNodeMap
{
    private readonly IReadOnlyDictionary<object, SandboxNodeDescriptor> _descriptors;

    private SandboxNodeMap(
        IReadOnlyList<SandboxNodeDescriptor> nodes,
        IReadOnlyDictionary<object, SandboxNodeDescriptor> descriptors)
    {
        Nodes = nodes;
        _descriptors = descriptors;
    }

    public IReadOnlyList<SandboxNodeDescriptor> Nodes { get; }

    public static SandboxNodeMap Create(SandboxModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var builder = new Builder();
        foreach (var function in module.Functions)
        {
            if (function is null)
            {
                throw new ArgumentException("Module functions cannot contain null entries.", nameof(module));
            }

            builder.AddFunction(function);
        }

        return builder.Build();
    }

    public SandboxNodeDescriptor GetDescriptor(SandboxFunction function)
        => GetDescriptorCore(function);

    public SandboxNodeDescriptor GetDescriptor(Statement statement)
        => GetDescriptorCore(statement);

    public SandboxNodeDescriptor GetDescriptor(Expression expression)
        => GetDescriptorCore(expression);

    private SandboxNodeDescriptor GetDescriptorCore(object node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _descriptors.TryGetValue(node, out var descriptor)
            ? descriptor
            : throw new ArgumentException("The node does not belong to this node map.", nameof(node));
    }

    private sealed class Builder
    {
        private readonly List<SandboxNodeDescriptor> _nodes = [];
        private readonly Dictionary<object, SandboxNodeDescriptor> _descriptors =
            new(ReferenceEqualityComparer.Instance);

        public void AddFunction(SandboxFunction function)
        {
            Add(function, function.Id, SandboxNodeKind.Function, "function", span: null);
            AddStatements(function, function.Body, "body");
        }

        public SandboxNodeMap Build()
            => new(
                new ReadOnlyCollection<SandboxNodeDescriptor>(_nodes.ToArray()),
                new ReadOnlyDictionary<object, SandboxNodeDescriptor>(_descriptors));

        private void AddStatements(SandboxFunction function, IReadOnlyList<Statement> statements, string path)
        {
            for (var index = 0; index < statements.Count; index++)
            {
                var statement = statements[index] ??
                    throw new ArgumentException("Statement collections cannot contain null entries.");
                AddStatement(function, statement, $"{path}/{index}");
            }
        }

        private void AddStatement(SandboxFunction function, Statement statement, string path)
        {
            Add(statement, function.Id, SandboxNodeKind.Statement, path, statement.Span);
            switch (statement)
            {
                case AssignmentStatement assignment:
                    AddExpression(function, assignment.Value, $"{path}/value");
                    break;
                case ReturnStatement returned:
                    AddExpression(function, returned.Value, $"{path}/value");
                    break;
                case ExpressionStatement expression:
                    AddExpression(function, expression.Value, $"{path}/value");
                    break;
                case IfStatement branch:
                    AddExpression(function, branch.Condition, $"{path}/condition");
                    AddStatements(function, branch.Then, $"{path}/then");
                    AddStatements(function, branch.Else, $"{path}/else");
                    break;
                case WhileStatement loop:
                    AddExpression(function, loop.Condition, $"{path}/condition");
                    AddStatements(function, loop.Body, $"{path}/body");
                    break;
                case ForRangeStatement range:
                    AddExpression(function, range.Start, $"{path}/start");
                    AddExpression(function, range.End, $"{path}/end");
                    AddStatements(function, range.Body, $"{path}/body");
                    break;
            }
        }

        private void AddExpression(SandboxFunction function, Expression expression, string path)
        {
            ArgumentNullException.ThrowIfNull(expression);
            Add(expression, function.Id, SandboxNodeKind.Expression, path, expression.Span);
            switch (expression)
            {
                case UnaryExpression unary:
                    AddExpression(function, unary.Operand, $"{path}/operand");
                    break;
                case BinaryExpression binary:
                    AddExpression(function, binary.Left, $"{path}/left");
                    AddExpression(function, binary.Right, $"{path}/right");
                    break;
                case CallExpression call:
                    for (var index = 0; index < call.Arguments.Count; index++)
                    {
                        AddExpression(function, call.Arguments[index], $"{path}/argument/{index}");
                    }

                    break;
            }
        }

        private void Add(object node, string functionId, SandboxNodeKind kind, string path, SourceSpan? span)
        {
            var descriptor = new SandboxNodeDescriptor(CreateId(functionId, path), functionId, kind, path, span);
            if (!_descriptors.TryAdd(node, descriptor))
            {
                throw new ArgumentException("Sandbox IR must be a tree and cannot reuse node instances.");
            }

            _nodes.Add(descriptor);
        }

        private static SandboxNodeId CreateId(string functionId, string path)
        {
            var identity = $"{SandboxNodeId.CurrentVersion}\0{functionId.Length}\0{functionId}\0{path}";
            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return new SandboxNodeId($"v{SandboxNodeId.CurrentVersion}:{Convert.ToHexStringLower(digest)}");
        }
    }
}
