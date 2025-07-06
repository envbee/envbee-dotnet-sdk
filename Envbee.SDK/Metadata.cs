namespace Envbee.SDK;

/// <summary>
/// Pagination / list metadata returned by the API.
/// </summary>
public sealed record Metadata(int Offset, int Limit, int Total);
