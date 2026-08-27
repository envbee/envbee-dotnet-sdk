using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Envbee.SDK.Exceptions;
using Envbee.SDK.Interfaces;
using Envbee.SDK.Tests.Testing;
using Xunit;

namespace Envbee.SDK.Tests;

public class EnvbeeClientMainTests
{
    private const string ENC_PREFIX = "envbee:enc:v1:";



    /* ---------- Fake handler configurable ---------- */
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage?>? Responder { get; set; }
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage?>>? AsyncResponder { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (AsyncResponder is not null)
            {
                var asyncResp = await AsyncResponder(req, ct);
                return asyncResp ?? new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            var resp = Responder?.Invoke(req) ??
                       new HttpResponseMessage(HttpStatusCode.InternalServerError);
            return resp;
        }
    }

    private static HttpResponseMessage JsonResp(object payload, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload),
                                                Encoding.UTF8, "application/json")
        };

    /* -------------------------------------------------
       1)  Variable simple (string)
    ---------------------------------------------------*/
    [Fact]
    public async Task GetVariable_SimpleString()
    {
        var client = CreateClient(h => JsonResp(new { value = "Value1" }));
        var value = await client.GetAsync("Var1");
        Assert.Equal("Value1", value);
    }

    /* 2) Variable numérica */
    [Fact]
    public async Task GetVariable_Number()
    {
        var client = CreateClient(h => JsonResp(new { value = 1234 }));
        var value1 = await client.GetAsync("Var1234");
        Assert.Equal(1234, Convert.ToInt32(value1));
        int value2 = await client.GetAsync<int>("Var1234");
        Assert.Equal(1234, value2);
    }

    /* 3) Cifrado pre‑generado (CLI) */
    [Fact]
    public async Task GetVariable_Encrypted_FromCli()
    {
        const string keyStr =
            "0123456789abcdef0123456789abcdef";
        const string encryptedValue =
            $"{ENC_PREFIX}d0ktKfDJB4CIPbRmXfOmVlCU8ZCx4fl/2eZtkjgbqJy3g569ZGDEqnVOP94pDfw2Jg==";

        var client = CreateClient(
            _ => JsonResp(new { value = encryptedValue }),
            encKey: keyStr);

        var value = await client.GetAsync("EncryptedVar");
        Assert.Equal("super-secret-password", value);
    }

    /* 4) Cifrado dinámico */
    [Fact]
    public async Task GetVariable_Encrypted_Dynamic()
    {
        var key = "0123456789abcdef0123456789abcdef"u8.ToArray();
        var aes = new AesGcm(key, 16);                         // .NET 8 ctor
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = "SuperSecretValue"u8.ToArray();
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plain, cipher, tag, null);

        var enc = ENC_PREFIX +
                  Convert.ToBase64String(nonce.Concat(cipher).Concat(tag).ToArray());

        var client = CreateClient(_ => JsonResp(new { value = enc }), encKey: key);
        var value = await client.GetAsync("EncryptedVar");
        Assert.Equal("SuperSecretValue", value);
    }

    /* 5) Cifrado sin clave -> excepción */
    [Fact]
    public async Task GetVariable_Encrypted_NoKey_Throws()
    {
        var key = "0123456789abcdef0123456789abcdef"u8.ToArray();
        var aes = new AesGcm(key, 16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var pt = "NoKeyErrorExpected"u8.ToArray();
        var cipher = new byte[pt.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, pt, cipher, tag, null);

        var enc = ENC_PREFIX + Convert.ToBase64String(
            nonce.Concat(cipher).Concat(tag).ToArray());

        var client = CreateClient(_ => JsonResp(new { value = enc }));   // sin encKey
        await Assert.ThrowsAsync<DecryptionException>(() => client.GetAsync("X"));
    }

    /* 6) Fallback a caché */
    [Fact]
    public async Task GetVariable_CacheFallback()
    {
        var cache = new MemoryCache();
        int call = 0;
        var client = CreateClient(_ =>
        {
            if (Interlocked.Increment(ref call) == 1)
                return JsonResp(new { value = "ValueFromCache" });
            return JsonResp(new { }, HttpStatusCode.InternalServerError);
        }, cacheStore: cache);

        var first = await client.GetAsync("Var1");
        var second = await client.GetAsync("Var1");
        Assert.Equal("ValueFromCache", first);
        Assert.Equal(first, second);      // proviene de cache
    }

    [Fact]
    public async Task GetVariable_CachePath_CustomDirectory()
    {
        var customCachePath = Path.Combine(Path.GetTempPath(), $"envbee-test-cache-{Guid.NewGuid()}");

        try
        {
            var client = CreateClient(
                _ => JsonResp(new { value = "CustomPathValue" }),
                cachePath: customCachePath);

            var value = await client.GetAsync("VAR_CUSTOM_CACHE");
            Assert.Equal("CustomPathValue", value);
            Assert.True(File.Exists(Path.Combine(customCachePath, "variables.json")));
        }
        finally
        {
            if (Directory.Exists(customCachePath))
                Directory.Delete(customCachePath, recursive: true);
        }
    }

    [Fact]
    public async Task GetVariable_FallsBackToMemory_WhenCachePathIsInvalid()
    {
        var fileAsDirectoryPath = Path.GetTempFileName();
        try
        {
            int call = 0;
            var client = CreateClient(_ =>
            {
                if (Interlocked.Increment(ref call) == 1)
                    return JsonResp(new { value = "ValueFromMemoryFallback" });

                return JsonResp(new { }, HttpStatusCode.InternalServerError);
            }, cachePath: fileAsDirectoryPath);

            var first = await client.GetAsync("VAR_MEMORY_FALLBACK");
            var second = await client.GetAsync("VAR_MEMORY_FALLBACK");

            Assert.Equal("ValueFromMemoryFallback", first);
            Assert.Equal("ValueFromMemoryFallback", second);
        }
        finally
        {
            if (File.Exists(fileAsDirectoryPath))
                File.Delete(fileAsDirectoryPath);
        }
    }

    [Fact]
    public async Task GetVariable_Timeout_FallsBackToCache()
    {
        var cache = new MemoryCache();
        cache.Set("VAR_TIMEOUT", "CachedTimeoutValue");

        var client = CreateClient(
            _ => null,
            asyncResponder: async (_, ct) =>
            {
                await Task.Delay(150, ct);
                return JsonResp(new { value = "TooLate" });
            },
            cacheStore: cache,
            timeoutSeconds: 0.01
        );

        var value = await client.GetAsync("VAR_TIMEOUT");
        Assert.Equal("CachedTimeoutValue", value);
    }

    [Fact]
    public async Task GetVariable_CustomTimeout_AllowsSlowResponse()
    {
        var client = CreateClient(
            _ => null,
            asyncResponder: async (_, ct) =>
            {
                await Task.Delay(30, ct);
                return JsonResp(new { value = "Value1" });
            },
            timeoutSeconds: 1.0
        );

        var value = await client.GetAsync("VAR_TIMEOUT_OK");
        Assert.Equal("Value1", value);
    }

    /* 7) get_variables simple */
    [Fact]
    public async Task GetVariables_Simple()
    {
        var payload = new
        {
            metadata = new { limit = 1, offset = 10, total = 100 },
            data = new[]
            {
                new { id = 1, type = "STRING",  name = "VAR1", description = "desc1" },
                new { id = 2, type = "BOOLEAN", name = "VAR2", description = "desc2" }
            }
        };

        var client = CreateClient(_ => JsonResp(payload));
        var (vars, md) = await client.GetVariablesAsync();

        Assert.Equal("desc1",
            vars.First(v => v.GetProperty("name").GetString() == "VAR1")
                .GetProperty("description").GetString());
        Assert.Equal(100, md.Total);
    }

    [Fact]
    public async Task GetVariablesTyped_Simple()
    {
        var payload = new
        {
            metadata = new { limit = 1, offset = 10, total = 100 },
            data = new[]
            {
                new { id = 1, type = "STRING",  name = "VAR1", description = "desc1" },
                new { id = 2, type = "BOOLEAN", name = "VAR2", description = "desc2" }
            }
        };

        var client = CreateClient(_ => JsonResp(payload));
        var (vars, md) = await client.GetVariablesTypedAsync();

        Assert.Equal("desc1", vars.First(v => v.Name == "VAR1").Description);
        Assert.Equal(VariableType.STRING, vars.First(v => v.Name == "VAR1").Type);
        Assert.Equal(100, md.Total);
    }

    [Fact]
    public async Task GetVariablesValuesTyped_Simple()
    {
        var payload = new
        {
            metadata = new { limit = 2, offset = 0, total = 2 },
            data = new object[]
            {
                new { id = 1, variable_id = 1, content = new Dictionary<string, object> { ["value"] = "Value1" } },
                new { id = 2, variable_id = 2, content = new Dictionary<string, object> { ["value"] = true } }
            }
        };

        var client = CreateClient(_ => JsonResp(payload));
        var (values, md) = await client.GetVariablesValuesTypedAsync();

        Assert.Equal("Value1", values.First(v => v.VariableId == 1).Content.GetProperty("value").GetString());
        Assert.True(values.First(v => v.VariableId == 2).Content.GetProperty("value").GetBoolean());
        Assert.Equal(2, md.Total);
    }

    [Fact]
    public async Task FillEnvVars_SetsEnvironmentVariables()
    {
        try
        {
            Environment.SetEnvironmentVariable("VAR1", null);
            Environment.SetEnvironmentVariable("VAR2", null);

            var client = CreateClient(req =>
            {
                var path = req.RequestUri!.AbsolutePath;

                if (path == "/v1/variables")
                {
                    return JsonResp(new
                    {
                        metadata = new { limit = 2, offset = 0, total = 2 },
                        data = new[]
                        {
                            new { id = 1, name = "VAR1", type = "STRING", description = "desc1" },
                            new { id = 2, name = "VAR2", type = "BOOLEAN", description = "desc2" }
                        }
                    });
                }

                if (path == "/v1/variables-values")
                {
                    return JsonResp(new
                    {
                        metadata = new { limit = 2, offset = 0, total = 2 },
                        data = new object[]
                        {
                            new { id = 101, variable_id = 1, content = new Dictionary<string, object> { ["value"] = "Value1" } },
                            new { id = 202, variable_id = 2, content = new Dictionary<string, object> { ["value"] = true } }
                        }
                    });
                }

                return JsonResp(new { }, HttpStatusCode.InternalServerError);
            });

            await client.FillEnvVarsAsync();

            Assert.Equal("Value1", Environment.GetEnvironmentVariable("VAR1"));
            Assert.Equal("True", Environment.GetEnvironmentVariable("VAR2"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAR1", null);
            Environment.SetEnvironmentVariable("VAR2", null);
        }
    }

    [Fact]
    public async Task FillEnvVars_UsesCacheFallback()
    {
        try
        {
            Environment.SetEnvironmentVariable("VAR1", null);
            Environment.SetEnvironmentVariable("VAR2", null);

            var cache = new MemoryCache();
            var failFillRequests = false;

            var client = CreateClient(req =>
            {
                var path = req.RequestUri!.AbsolutePath;

                if (failFillRequests && (path == "/v1/variables" || path == "/v1/variables-values"))
                    return JsonResp(new { }, HttpStatusCode.InternalServerError);

                if (path == "/v1/variables-values-by-name/VAR1/content")
                    return JsonResp(new { value = "ValueFromCache1" });

                if (path == "/v1/variables-values-by-name/VAR2/content")
                    return JsonResp(new { value = true });

                if (path == "/v1/variables" || path == "/v1/variables-values")
                    return JsonResp(new { }, HttpStatusCode.InternalServerError);

                return JsonResp(new { }, HttpStatusCode.InternalServerError);
            }, cacheStore: cache);

            await client.GetAsync("VAR1");
            await client.GetAsync("VAR2");

            failFillRequests = true;
            await client.FillEnvVarsAsync();

            Assert.Equal("ValueFromCache1", Environment.GetEnvironmentVariable("VAR1"));
            Assert.Equal("True", Environment.GetEnvironmentVariable("VAR2"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAR1", null);
            Environment.SetEnvironmentVariable("VAR2", null);
        }
    }

    /* ---------- helper ---------- */
    private static EnvbeeClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage?> responder,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage?>>? asyncResponder = null,
        Secret? encKey = null,
        ICacheStore? cacheStore = null,
        string? cachePath = null,
        double? timeoutSeconds = null)
    {
        var handler = new FakeHttpHandler { Responder = responder, AsyncResponder = asyncResponder };

        // Build the client fluently
        var builder = EnvbeeClientBuilder.Create()
            .WithEndpoint("https://mocked")
            .WithCredentials("1__local", "key---1")
            .WithHttpMessageHandler(handler);

        if (encKey is { } key)
            builder.WithEncryptionKey(key);

        if (!string.IsNullOrEmpty(cachePath))
            builder.WithCachePath(cachePath);

        if (timeoutSeconds.HasValue)
            builder.WithTimeoutSeconds(timeoutSeconds.Value);

        var client = builder.Build();


        // Inject in‑memory cache (reflection) if requested
        if (cacheStore is not null)
        {
            typeof(EnvbeeClient)
                .GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(client, cacheStore);
        }

        return client;
    }
}

/* -------- Extension used above to conditionally apply builder steps -------- */
internal static class BuilderExt
{
    public static EnvbeeClientBuilder ApplyIf(
        this EnvbeeClientBuilder builder, bool condition, Func<EnvbeeClientBuilder, EnvbeeClientBuilder> then)
        => condition ? then(builder) : builder;
}
