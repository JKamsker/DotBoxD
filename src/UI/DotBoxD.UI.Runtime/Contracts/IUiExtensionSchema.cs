using System.Text.Json;

namespace DotBoxD.UI.Runtime;

/// <summary>Trusted host registration, never sent by a plugin. Reject unknown fields and limit data.</summary>
public interface IUiExtensionSchema
{
    string SchemaId { get; }
    void Validate(JsonElement payload);
}
