using System.Collections.Concurrent;
using Envbee.SDK.Interfaces;

namespace Envbee.SDK.Internal;

internal sealed class MemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, string> _dict = new();

    public string? Get(string key) => _dict.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => _dict[key] = value;

    public IEnumerable<KeyValuePair<string, string>> GetAll() => _dict.ToArray();
}
