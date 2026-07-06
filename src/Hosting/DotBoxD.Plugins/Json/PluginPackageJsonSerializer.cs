using System.Buffers;
using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Kernels.Serialization.Json.Internal;
using DotBoxD.Plugins.Runtime;
using DotBoxD.Plugins.Runtime.Rpc;

namespace DotBoxD.Plugins.Json;

using static JsonImport;

public static class PluginPackageJsonSerializer
{
    public static string Export(PluginPackage package, bool indented = false)
    {
        ArgumentNullException.ThrowIfNull(package);
        PluginPackageJsonWriter.ValidatePackageForExport(package);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = indented });
        PluginPackageJsonWriter.WritePackage(writer, package);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static PluginPackage Import(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            JsonImportBudgetGuard.Validate(json);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });

            var package = PluginPackageJsonReader.ReadPackage(document.RootElement);
            // A server extension package has its own shape (no event subscription/contract), so it is
            // validated by RpcKernelPackageValidator instead of the event-kernel validator.
            if (package.Manifest.RpcEntrypoint is not null)
            {
                RpcKernelPackageValidator.Validate(package);
            }
            else
            {
                PluginPackageValidator.Validate(package);
            }

            return package;
        }
        catch (JsonException ex)
        {
            throw Error("E-JSON-INVALID", ex.Message);
        }
        catch (FormatException ex)
        {
            throw Error("E-JSON-VERSION", ex.Message);
        }
    }

}
