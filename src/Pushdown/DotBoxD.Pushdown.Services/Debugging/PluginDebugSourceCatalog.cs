using System.Text;
using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Model;
using DotBoxD.Plugins;

namespace DotBoxD.Pushdown.Services;

internal sealed class PluginDebugSourceCatalog(Func<string, byte[]?> sourceReader)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PackageSources> _packages = new(StringComparer.Ordinal);

    public void Register(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var sources = package.DebugInfo is null
            ? VirtualSources(package)
            : MappedSources(package, package.DebugInfo);
        lock (_gate)
        {
            _packages[package.Manifest.PluginId] = sources;
        }
    }

    public SourceResolution Resolve(string pluginId, string path, IReadOnlyList<int> lines)
    {
        var normalized = Normalize(path);
        var packages = Packages(pluginId);
        if (packages.Count == 0)
        {
            throw new KeyNotFoundException($"Plugin source map '{pluginId}' is not registered.");
        }

        var document = packages.SelectMany(item => item.Documents)
            .FirstOrDefault(item => PathsEqual(item.Path, normalized));
        var content = packages.FirstOrDefault(item => PathsEqual(item.VirtualPath, normalized))?.VirtualSource;
        var breakpoints = lines.Select(line => ResolveLine(line, packages, normalized)).ToArray();
        return new SourceResolution(document?.Path ?? path, content, breakpoints);
    }

    public string? Source(string pluginId, string path)
    {
        lock (_gate)
        {
            return SelectPackages(pluginId)
                .FirstOrDefault(package => PathsEqual(package.VirtualPath, Normalize(path)))?.VirtualSource;
        }
    }

    public SourceLocation? Location(string pluginId, string nodeId)
    {
        PackageSources package;
        lock (_gate)
        {
            package = SelectPackages(pluginId)
                .FirstOrDefault(item => item.Points.Any(point => point.NodeId.Value == nodeId))!;
            if (package is null)
            {
                return null;
            }
        }

        var point = package.Points.FirstOrDefault(item => item.NodeId.Value == nodeId);
        if (point?.Span.DocumentId is null)
        {
            return null;
        }

        var document = package.Documents.FirstOrDefault(item => item.Id == point.Span.DocumentId);
        return document is null
            ? null
            : new SourceLocation(
                document.Path,
                point.Span.Line,
                point.Span.Column,
                point.Span.EndLine,
                point.Span.EndColumn,
                document.IsVirtual,
                package.VariableBindings
                    .Where(binding => string.Equals(
                        binding.FunctionId,
                        package.Functions.GetValueOrDefault(point.NodeId),
                        StringComparison.Ordinal))
                    .ToArray());
    }

    private bool MatchesChecksum(SourceDocument document)
    {
        try
        {
            var bytes = sourceReader(document.Path);
            return bytes is not null && document.Document.MatchesSourceBytes(bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private ResolvedBreakpoint ResolveLine(
        int line,
        IReadOnlyList<PackageSources> packages,
        string normalizedPath)
    {
        var candidates = new List<(ResolvedNodeBinding Binding, int Line, int Column)>();
        var hadStaleDocument = false;
        foreach (var package in packages)
        {
            var documents = package.Documents.Where(item => PathsEqual(item.Path, normalizedPath)).ToArray();
            if (documents.Length == 0)
            {
                continue;
            }

            foreach (var document in documents)
            {
                if (!document.IsVirtual && !MatchesChecksum(document))
                {
                    hadStaleDocument = true;
                    continue;
                }

                var points = package.Points
                    .Where(point => string.Equals(point.Span.DocumentId, document.Id, StringComparison.Ordinal))
                    .Where(point => point.Span.SequencePointKind != SourceSequencePointKind.Hidden)
                    .Where(point => point.Span.Line == line || point.Span.Line == line + 1)
                    .GroupBy(point => package.Functions.GetValueOrDefault(point.NodeId, point.NodeId.Value), StringComparer.Ordinal)
                    .Select(group => group.First());
                foreach (var point in points)
                {
                    var functionId = package.Functions.GetValueOrDefault(point.NodeId);
                    candidates.Add((
                        new ResolvedNodeBinding(
                            package.PluginId,
                            point.NodeId.Value,
                            package.VariableBindings
                                .Where(binding => string.Equals(
                                    binding.FunctionId,
                                    functionId,
                                    StringComparison.Ordinal))
                                .ToArray()),
                        point.Span.Line,
                        point.Span.Column));
                }
            }
        }

        var resolvedLine = candidates.Count == 0 ? line : candidates.Min(candidate => candidate.Line);
        var selected = candidates.Where(candidate => candidate.Line == resolvedLine).ToArray();
        var distinct = selected.Select(candidate => candidate.Binding)
            .DistinctBy(binding => (binding.PluginId, binding.NodeId))
            .ToArray();
        return distinct.Length > 0
            ? new ResolvedBreakpoint(
                resolvedLine,
                selected.Min(candidate => candidate.Column),
                distinct[0].NodeId,
                Verified: true,
                null,
                distinct)
            : hadStaleDocument
                ? new ResolvedBreakpoint(line, 0, null, Verified: false, "Source checksum differs from the built plugin package.", [])
                : Unmapped(line);
    }

    private static ResolvedBreakpoint Unmapped(int line) =>
        new(line, 0, null, Verified: false, "No executable kernel sequence point exists on this line.", []);

    private static PackageSources MappedSources(PluginPackage package, KernelDebugInfo info)
    {
        var documents = info.Documents.Select(document => new SourceDocument(document, IsVirtual: false)).ToArray();
        var functions = SandboxNodeMap.Create(package.Module).Nodes.ToDictionary(node => node.Id, node => node.FunctionId);
        return new PackageSources(
            package.Manifest.PluginId,
            documents,
            info.SequencePoints,
            functions,
            info.VariableBindings,
            null,
            null);
    }

    private static PackageSources VirtualSources(PluginPackage package)
    {
        var path = $"dotboxd-ir://{package.Manifest.PluginId}/module.ir";
        var nodes = SandboxNodeMap.Create(package.Module).Nodes;
        var text = new StringBuilder();
        var points = new KernelSequencePoint[nodes.Count];
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            text.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(": ").Append(node.Kind).Append(' ').Append(node.FunctionId)
                .Append(' ').Append(node.StructuralPath).Append(" // ").AppendLine(node.Id.Value);
            points[index] = new KernelSequencePoint(
                node.Id,
                new SourceSpan(index + 1, 1, "virtual", index + 1, 2));
        }

        var source = text.ToString();
        var document = KernelDebugDocument.FromSource("virtual", path, source);
        return new PackageSources(
            package.Manifest.PluginId,
            [new SourceDocument(document, IsVirtual: true)],
            points,
            nodes.ToDictionary(node => node.Id, node => node.FunctionId),
            [],
            path,
            source);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private IReadOnlyList<PackageSources> Packages(string pluginId)
    {
        lock (_gate)
        {
            return SelectPackages(pluginId).ToArray();
        }
    }

    private IEnumerable<PackageSources> SelectPackages(string pluginId)
        => string.IsNullOrWhiteSpace(pluginId) || pluginId == "*"
            ? _packages.Values
            : _packages.TryGetValue(pluginId, out var package) ? [package] : [];

    private static bool PathsEqual(string? path, string normalized)
        => path is not null && string.Equals(Normalize(path), normalized, StringComparison.OrdinalIgnoreCase);

    private sealed record SourceDocument(KernelDebugDocument Document, bool IsVirtual)
    {
        public string Id => Document.Id;

        public string Path => Document.Path;
    }

    private sealed record PackageSources(
        string PluginId,
        IReadOnlyList<SourceDocument> Documents,
        IReadOnlyList<KernelSequencePoint> Points,
        IReadOnlyDictionary<SandboxNodeId, string> Functions,
        IReadOnlyList<KernelDebugVariableBinding> VariableBindings,
        string? VirtualPath,
        string? VirtualSource);
}

internal sealed record SourceResolution(
    string Path,
    string? Content,
    IReadOnlyList<ResolvedBreakpoint> Breakpoints);

internal sealed record ResolvedBreakpoint(
    int Line,
    int Column,
    string? NodeId,
    bool Verified,
    string? Message,
    IReadOnlyList<ResolvedNodeBinding> Bindings);

internal sealed record ResolvedNodeBinding(
    string PluginId,
    string NodeId,
    IReadOnlyList<KernelDebugVariableBinding> VariableBindings);

internal sealed record SourceLocation(
    string Path,
    int Line,
    int Column,
    int? EndLine,
    int? EndColumn,
    bool IsVirtual,
    IReadOnlyList<KernelDebugVariableBinding> VariableBindings);
