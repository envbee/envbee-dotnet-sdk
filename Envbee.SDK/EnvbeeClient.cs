using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Envbee.SDK.Exceptions;
using Envbee.SDK.Interfaces;
using Envbee.SDK.Internal;
using Microsoft.Extensions.Logging;

namespace Envbee.SDK;

/// <summary>
/// High‑level Envbee API client.
/// Mirrors the original Python implementation.
/// </summary>
public sealed class EnvbeeClient
{
    private const string EnvarApiKey = "ENVBEE_API_KEY";
    private const string EnvarApiSecret = "ENVBEE_API_SECRET";
    private const string EnvarApiUrl = "ENVBEE_API_URL";
    private const string EnvarEncKey = "ENVBEE_ENC_KEY";

    private const string DefaultBaseUrl = "https://api.envbee.dev";

    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;     // raw bytes
    private readonly AesGcm? _aesGcm;
    private readonly ILogger _logger;
    private readonly ICacheStore _cache;
    private readonly TimeSpan _requestTimeout;
    private static HttpClient _http = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly JsonSerializerOptions serializerOptions = new() { PropertyNameCaseInsensitive = true };

    internal static void OverrideHttpClient(HttpClient custom)
    {
        if (custom is null) throw new ArgumentNullException(nameof(custom));
        custom.Timeout = Timeout.InfiniteTimeSpan;
        _http = custom;
    }


    #region ctor
    /// <summary>
    /// Envbee API client used to retrieve and decrypt variables.
    /// </summary>
    public EnvbeeClient(
        string? apiKey = null,
        Secret apiSecret = default,
        Secret encKey = default,
        string? baseUrl = null,
        string? cachePath = null,
        double? timeoutSeconds = null)
    {
        _logger = LoggerFactory.Create(b => b.AddDebug()).CreateLogger<EnvbeeClient>();

        _baseUrl = baseUrl ?? Environment.GetEnvironmentVariable(EnvarApiUrl) ?? DefaultBaseUrl;

        _apiKey = apiKey ?? Environment.GetEnvironmentVariable(EnvarApiKey)
            ?? throw new ArgumentException("API key must be provided or ENVBEE_API_KEY must be set.");

        if (apiSecret.IsEmpty)
            apiSecret = Environment.GetEnvironmentVariable(EnvarApiSecret);

        _apiSecret = apiSecret.Data ?? throw new ArgumentException("API secret must be provided or ENVBEE_API_SECRET must be set.");

        if (encKey.IsEmpty)
            encKey = Environment.GetEnvironmentVariable(EnvarEncKey);

        if (!encKey.IsEmpty)
        {
            if (encKey.IsCreatedFromString)
                encKey = SHA256.HashData(encKey.Data);

            if (encKey.Length is 16 or 24 or 32)
                _aesGcm = new AesGcm(encKey.Data, 16);
            else
                throw new ArgumentException("Encryption key must be 16, 24 or 32 bytes.");
        }
        else
        {
            _logger.LogDebug("No encryption key provided");
        }

        var parsedTimeout = timeoutSeconds.GetValueOrDefault(4);
        _requestTimeout = TimeSpan.FromSeconds(parsedTimeout > 0 ? parsedTimeout : 4);

        _cache = CreateCacheStore(_apiKey, cachePath);

        _logger.LogInformation("EnvbeeClient initialized for {BaseUrl}.", _baseUrl);
    }
    #endregion

    private ICacheStore CreateCacheStore(string apiKey, string? cachePath)
    {
        try
        {
            return new FileCache(apiKey, _logger, cachePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache path is unavailable. Falling back to in-memory cache only.");
            return new MemoryCacheStore();
        }
    }

    #region public API

    /// <summary>
    /// Get SDK Version
    /// </summary>
    public static string Version =>
    typeof(EnvbeeClient).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "unknown";


    /// <summary>
    /// Get a variable's value knowing its type (string, int or bool)
    /// </summary>
    public async Task<T?> GetAsync<T>(string name, CancellationToken ct = default)
    {
        var raw = await GetInternalAsync(name, ct);

        return raw switch
        {
            null => default,
            T v => v,
            string s when typeof(T) == typeof(int) && int.TryParse(s, out var i) => (T)(object)i,
            string s when typeof(T) == typeof(double) && double.TryParse(s, out var i) => (T)(object)i,
            string s when typeof(T) == typeof(decimal) && decimal.TryParse(s, out var d) => (T)(object)d,
            string s when typeof(T) == typeof(bool) && bool.TryParse(s, out var b) => (T)(object)b,
            _ => throw new InvalidCastException(
                     $"Value '{raw}' cannot be converted to {typeof(T).Name}")
        };
    }

    /// <summary>
    /// Get a variable using async call
    /// </summary>
    public Task<object?> GetAsync(string variableName, CancellationToken ct = default)
        => GetInternalAsync(variableName, ct);

    /// <summary>
    /// Fetch a paginated list of variables.
    /// </summary>
    public async Task<(IReadOnlyList<JsonElement> Data, Metadata Meta)> GetVariablesAsync(
        int? offset = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var path = "/v1/variables";
        var query = new Dictionary<string, object?>();
        if (offset.HasValue) query["offset"] = offset;
        if (limit.HasValue) query["limit"] = limit;
        path = UrlHelpers.AddQueryString(path, query);

        var json = await SendRequestAsync(path, ct);
        var meta = JsonSerializer.Deserialize<Metadata>(json.GetProperty("metadata").GetRawText(), serializerOptions);
        var data = json.GetProperty("data").EnumerateArray().ToList();

        return (data, meta!);
    }

    /// <summary>
    /// Fetch a paginated list of typed variables.
    /// </summary>
    public async Task<(IReadOnlyList<Variable> Data, Metadata Meta)> GetVariablesTypedAsync(
        int? offset = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var (rawData, meta) = await GetVariablesAsync(offset, limit, ct);
        var data = rawData.Select(ParseVariable).ToList();
        return (data, meta);
    }

    /// <summary>
    /// Fetch a paginated list of variables values.
    /// </summary>
    public async Task<(IReadOnlyList<JsonElement> Data, Metadata Meta)> GetVariablesValuesAsync(
        int? offset = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var path = "/v1/variables-values";
        var query = new Dictionary<string, object?>();
        if (offset.HasValue) query["offset"] = offset;
        if (limit.HasValue) query["limit"] = limit;
        path = UrlHelpers.AddQueryString(path, query);

        var json = await SendRequestAsync(path, ct);
        var meta = JsonSerializer.Deserialize<Metadata>(json.GetProperty("metadata").GetRawText(), serializerOptions);
        var data = json.GetProperty("data").EnumerateArray().ToList();

        return (data, meta!);
    }

    /// <summary>
    /// Fetch a paginated list of typed variable values.
    /// </summary>
    public async Task<(IReadOnlyList<VariableValue> Data, Metadata Meta)> GetVariablesValuesTypedAsync(
        int? offset = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var (rawData, meta) = await GetVariablesValuesAsync(offset, limit, ct);
        var data = rawData.Select(ParseVariableValue).ToList();
        return (data, meta);
    }

    /// <summary>
    /// Fills process environment variables using envbee definitions and values.
    /// If API calls fail, falls back to locally cached values.
    /// </summary>
    public async Task FillEnvVarsAsync(IReadOnlyCollection<string>? variableNames = null, CancellationToken ct = default)
    {
        try
        {
            var allVariables = (await GetVariablesTypedAsync(ct: ct)).Data;
            var allValues = (await GetVariablesValuesTypedAsync(ct: ct)).Data
                .ToDictionary(v => v.VariableId, v => v);

            foreach (var variable in allVariables)
            {
                var name = variable.Name;

                if (variableNames is not null && !variableNames.Contains(name))
                {
                    _logger.LogDebug("Skipping variable {Var} as it's not in the specified list.", name);
                    continue;
                }

                try
                {
                    if (!allValues.TryGetValue(variable.Id, out var valueEntry))
                    {
                        _logger.LogWarning("Variable {Var} has no associated value entry.", name);
                        continue;
                    }

                    if (!valueEntry.Content.TryGetProperty("value", out var rawValue))
                    {
                        _logger.LogWarning("Variable {Var} has invalid value payload.", name);
                        continue;
                    }

                    if (rawValue.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                        continue;

                    var finalValue = rawValue.ValueKind == JsonValueKind.String
                        ? MaybeDecrypt(rawValue.GetString() ?? string.Empty)
                        : rawValue.ToString();

                    Environment.SetEnvironmentVariable(name, finalValue);
                    _logger.LogDebug("Set environment variable: {Var}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching or setting variable {Var}", name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fill env vars from API. Falling back to cache.");
            try
            {
                foreach (var kv in _cache.GetAll())
                {
                    var name = kv.Key;
                    if (variableNames is not null && !variableNames.Contains(name))
                    {
                        _logger.LogDebug("Skipping variable {Var} as it's not in the specified list.", name);
                        continue;
                    }

                    var cached = kv.Value;
                    var finalValue = MaybeDecrypt(cached);
                    Environment.SetEnvironmentVariable(name, finalValue);
                    _logger.LogDebug("Set environment variable from cache: {Var}", name);
                }
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Failed to fill environment variables from API and cache.");
            }
        }
    }
    #endregion

    #region internals
    private async Task<object?> GetInternalAsync(string name, CancellationToken ct)
    {
        var path = $"/v1/variables-values-by-name/{name}/content";

        try
        {
            var json = await SendRequestAsync(path, ct);
            var elem = json.GetProperty("value");

            object? value = elem.ValueKind switch
            {
                JsonValueKind.String => elem.GetString(),
                JsonValueKind.Number =>
                    elem.TryGetInt32(out var i32) ? i32 :
                    (elem.TryGetInt64(out var i64) ? i64 :
                    (elem.TryGetDouble(out var d) ? d :
                    elem.GetDecimal())),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };

            if (value is not null)
                _cache.Set(name, value.ToString()!);

            if (value is string s)
                value = MaybeDecrypt(s);

            return value;
        }
        catch (DecryptionException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch variable {Var}. Falling back to cache.", name);

            var cached = _cache.Get(name);
            return cached is null ? null : MaybeDecrypt(cached);
        }
    }

    private async Task<JsonElement> SendRequestAsync(string path, CancellationToken ct)
    {
        var hmacHeader = GenerateHmacHeader(path);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{path}");
        req.Headers.Add("Authorization", hmacHeader);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("x-envbee-client", "dotnet-sdk/0.1.0");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_requestTimeout);
            using var resp = await _http.SendAsync(req, timeoutCts.Token);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var stream = await resp.Content.ReadAsStreamAsync(timeoutCts.Token);
                var json = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
                return json.RootElement.Clone();
            }

            var payload = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new RequestException(resp.StatusCode, $"Request failed: {payload}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new RequestTimeoutException($"Request to {_baseUrl}{path} timed out.");
        }
    }

    private string GenerateHmacHeader(string urlPath)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var h = new HMACSHA256(_apiSecret);
        var md5 = MD5.HashData(Encoding.UTF8.GetBytes("{}"));

        void Update(ReadOnlySpan<char> s) => h.TransformBlock(
            Encoding.UTF8.GetBytes(s.ToString()), 0, s.Length, null, 0);

        Update(ts);
        Update("GET");
        Update(urlPath);
        Update(Convert.ToHexString(md5).ToLower());

        h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        var sig = Convert.ToHexString(h.Hash!).ToLower();

        return $"HMAC {ts}:{sig}";
    }

    private string MaybeDecrypt(string value)
    {
        if (!value.StartsWith(Constants.EncPrefix, StringComparison.Ordinal))
            return value;

        if (_aesGcm is null)
            throw new DecryptionException("Encrypted variable received but no key configured.");

        try
        {
            var raw = Convert.FromBase64String(value[Constants.EncPrefix.Length..]);
            var nonce = raw.AsSpan(0, 12);
            var data = raw.AsSpan(12);

            // ciphertext | tag (16 bytes at the end)
            Span<byte> plaintext = stackalloc byte[data.Length - 16];
            var cipher = data[..^16];
            var tag = data[^16..];
            _aesGcm.Decrypt(nonce, cipher, tag, plaintext, null);

            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException ex)
        {
            throw new DecryptionException("Decryption failed. Invalid key or corrupted data.", ex);
        }
    }

    private static Variable ParseVariable(JsonElement elem)
    {
        var id = elem.GetProperty("id").GetInt64();
        var name = elem.GetProperty("name").GetString()
            ?? throw new JsonException("Variable name is required.");
        var typeRaw = elem.GetProperty("type").GetString()
            ?? throw new JsonException("Variable type is required.");

        if (!Enum.TryParse<VariableType>(typeRaw, ignoreCase: true, out var variableType))
            throw new JsonException($"Unknown variable type: {typeRaw}");

        string? description = null;
        if (elem.TryGetProperty("description", out var descriptionElem) &&
            descriptionElem.ValueKind != JsonValueKind.Null)
        {
            description = descriptionElem.GetString();
        }

        return new Variable(id, variableType, name, description);
    }

    private static VariableValue ParseVariableValue(JsonElement elem)
    {
        var id = elem.GetProperty("id").GetInt64();
        var variableId = elem.GetProperty("variable_id").GetInt64();
        var content = elem.GetProperty("content").Clone();
        return new VariableValue(id, variableId, content);
    }
    #endregion
}
