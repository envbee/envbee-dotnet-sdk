namespace Envbee.SDK.Exceptions;

/// <summary>
/// Thrown when an HTTP request exceeds the configured timeout.
/// </summary>
public sealed class RequestTimeoutException(string message) : TimeoutException(message)
{
}
