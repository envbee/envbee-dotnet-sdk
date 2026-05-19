using System.Text.Json;
using Envbee.SDK.Interfaces;
using Microsoft.Extensions.Logging;

namespace Envbee.SDK.Internal;

/// <summary>
/// JSON‑file backed cache: saves a dictionary per API key under the user's
/// local application data folder.
/// </summary>
internal sealed class FileCache : ICacheStore
{
    private readonly string _filePath;
    private readonly ILogger _logger;
    private readonly object _lock = new();

    public FileCache(string apiKey, ILogger logger, string? cachePath = null)
    {
        var dir = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "envbee", apiKey, "cache");

        Directory.CreateDirectory(dir);

        var probePath = Path.Combine(dir, $".envbee-write-test-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        File.WriteAllText(probePath, "ok");
        File.Delete(probePath);

        _filePath = Path.Combine(dir, "variables.json");
        _logger = logger;
    }

    public string? Get(string key)
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath)) return null;
                var json = File.ReadAllText(_filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict?.TryGetValue(key, out var v) == true ? v : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read cache.");
            return null;
        }
    }

    public void Set(string key, string value)
    {
        try
        {
            lock (_lock)
            {
                Dictionary<string, string> dict = File.Exists(_filePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_filePath))!
                    : new();

                dict[key] = value;
                File.WriteAllText(_filePath, JsonSerializer.Serialize(dict));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write cache.");
        }
    }

    public IEnumerable<KeyValuePair<string, string>> GetAll()
    {
        try
        {
            lock (_lock)
            {
                if (!File.Exists(_filePath))
                    return Enumerable.Empty<KeyValuePair<string, string>>();

                var json = File.ReadAllText(_filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return dict?.ToArray() ?? Enumerable.Empty<KeyValuePair<string, string>>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate cache.");
            return Enumerable.Empty<KeyValuePair<string, string>>();
        }
    }
}
