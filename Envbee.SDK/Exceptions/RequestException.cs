using System.Net;

namespace Envbee.SDK.Exceptions;

/// <summary>
/// Thrown for non‑200 responses returned by the Envbee API.
/// </summary>
public sealed class RequestException(HttpStatusCode statusCode, string message) : Exception(message)
{
    /// <summary>
    /// Status code from the request
    /// </summary>
    public HttpStatusCode StatusCode { get; } = statusCode;
}
