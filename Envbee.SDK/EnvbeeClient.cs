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
    private static HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    private static readonly JsonSerializerOptions serializerOptions = new() { PropertyNameCaseInsensitive = true };

    internal static void OverrideHttpClient(HttpClient custom)
    {
        _http = custom ?? throw new ArgumentNullException(nameof(custom));
    }


    #region ctor
    /// <summary>
    /// Envbee API client used to retrieve and decrypt variables.
    /// </summary>
    public EnvbeeClient(
        string? apiKey = null,
        Secret apiSecret = default,
        Secret encKey = default,
        string? baseUrl = null)
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

        _cache = new FileCache(_apiKey, _logger);

        _logger.LogInformation("EnvbeeClient initialized for {BaseUrl}.", _baseUrl);
    }
    #endregion

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
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var stream = await resp.Content.ReadAsStreamAsync(ct);
                var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                return json.RootElement.Clone();
            }

            var payload = await resp.Content.ReadAsStringAsync(ct);
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
    #endregion
}
