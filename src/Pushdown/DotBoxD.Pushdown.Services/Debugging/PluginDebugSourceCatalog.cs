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
            : MappedSources(package.DebugInfo);
        lock (_gate)
        {
            _packages[package.Manifest.PluginId] = sources;
        }
    }

    public SourceResolution Resolve(string pluginId, string path, IReadOnlyList<int> lines)
    {
        PackageSources package;
        lock (_gate)
        {
            package = _packages.TryGetValue(pluginId, out var found)
                ? found
                : throw new KeyNotFoundException($"Plugin source map '{pluginId}' is not registered.");
        }

        var normalized = Normalize(path);
        var document = package.Documents.FirstOrDefault(item =>
            string.Equals(Normalize(item.Path), normalized, StringComparison.OrdinalIgnoreCase));
        if (document is null)
        {
            return new SourceResolution(path, package.VirtualSource, lines.Select(Unmapped).ToArray());
        }

        var checksumMatches = document.IsVirtual || MatchesChecksum(document);
        var points = package.Points
            .Where(point => string.Equals(point.Span.DocumentId, document.Id, StringComparison.Ordinal))
            .Where(point => point.Span.SequencePointKind != SourceSequencePointKind.Hidden)
            .ToArray();
        var breakpoints = lines.Select(line => ResolveLine(line, points, checksumMatches)).ToArray();
        return new SourceResolution(document.Path, document.IsVirtual ? package.VirtualSource : null, breakpoints);
    }

    public string? Source(string pluginId, string path)
    {
        lock (_gate)
        {
            if (!_packages.TryGetValue(pluginId, out var package))
            {
                return null;
            }

            return string.Equals(Normalize(package.VirtualPath ?? string.Empty), Normalize(path), StringComparison.Ordinal)
                ? package.VirtualSource
                : null;
        }
    }

    public SourceLocation? Location(string pluginId, string nodeId)
    {
        PackageSources package;
        lock (_gate)
        {
            if (!_packages.TryGetValue(pluginId, out package!))
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
                document.IsVirtual);
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

    private static ResolvedBreakpoint ResolveLine(
        int line,
        IReadOnlyList<KernelSequencePoint> points,
        bool checksumMatches)
    {
        var point = points
            .Where(candidate => candidate.Span.Line == line)
            .OrderBy(candidate => candidate.Span.Column)
            .FirstOrDefault();
        if (point is null)
        {
            return Unmapped(line);
        }

        return checksumMatches
            ? new ResolvedBreakpoint(line, point.Span.Column, point.NodeId.Value, Verified: true, null)
            : new ResolvedBreakpoint(line, point.Span.Column, null, Verified: false, "Source checksum differs from the built plugin package.");
    }

    private static ResolvedBreakpoint Unmapped(int line) =>
        new(line, 0, null, Verified: false, "No executable kernel sequence point exists on this line.");

    private static PackageSources MappedSources(KernelDebugInfo info)
    {
        var documents = info.Documents.Select(document => new SourceDocument(document, IsVirtual: false)).ToArray();
        return new PackageSources(documents, info.SequencePoints, null, null);
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
                new SourceSpan(index, 0, "virtual", index, 1));
        }

        var source = text.ToString();
        var document = KernelDebugDocument.FromSource("virtual", path, source);
        return new PackageSources(
            [new SourceDocument(document, IsVirtual: true)],
            points,
            path,
            source);
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private sealed record SourceDocument(KernelDebugDocument Document, bool IsVirtual)
    {
        public string Id => Document.Id;

        public string Path => Document.Path;
    }

    private sealed record PackageSources(
        IReadOnlyList<SourceDocument> Documents,
        IReadOnlyList<KernelSequencePoint> Points,
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
    string? Message);

internal sealed record SourceLocation(
    string Path,
    int Line,
    int Column,
    int? EndLine,
    int? EndColumn,
    bool IsVirtual);
