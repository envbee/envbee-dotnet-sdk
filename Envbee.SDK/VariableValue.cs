using System.Text.Json;

namespace Envbee.SDK;

/// <summary>
/// Variable value entry.
/// </summary>
public sealed record VariableValue(long Id, long VariableId, JsonElement Content);
