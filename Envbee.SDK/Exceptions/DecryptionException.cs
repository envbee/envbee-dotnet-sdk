namespace Envbee.SDK.Exceptions;

/// <summary>
/// Thrown when AES‑GCM decryption fails (invalid key or tampered data).
/// </summary>
public sealed class DecryptionException(string message, Exception? inner = null) : Exception(message, inner)
{
}
