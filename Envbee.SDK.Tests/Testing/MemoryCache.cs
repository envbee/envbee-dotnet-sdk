using System.Collections.Concurrent;
using Envbee.SDK.Interfaces;

namespace Envbee.SDK.Tests.Testing;

public sealed class MemoryCache : ICacheStore
{
    private readonly ConcurrentDictionary<string, string> _dict = new();
    public string? Get(string k) => _dict.TryGetValue(k, out var v) ? v : null;
    public void Set(string k, string v) => _dict[k] = v;
}
