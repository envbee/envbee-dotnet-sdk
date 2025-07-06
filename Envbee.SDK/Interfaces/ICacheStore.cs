namespace Envbee.SDK.Interfaces;

/// <summary>
/// Tiny pluggable cache abstraction to mimic Python's diskcache behaviour.
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Get a value from the internal store
    /// </summary>
    string? Get(string key);

    /// <summary>
    /// Sets a value to the internal store
    /// </summary>
    void Set(string key, string value);
}
