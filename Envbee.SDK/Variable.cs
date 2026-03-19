namespace Envbee.SDK;

/// <summary>
/// Variable definition.
/// </summary>
public sealed record Variable(long Id, VariableType Type, string Name, string? Description);
