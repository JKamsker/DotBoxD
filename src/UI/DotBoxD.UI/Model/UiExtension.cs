namespace DotBoxD.UI;

/// <summary>Stable schema identity and bounded data; executable renderer code stays host-owned.</summary>
public sealed record UiExtension(int NodeId, string SchemaId, string PayloadJson);
