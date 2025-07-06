using Envbee.SDK;
using Xunit;

public class EnvbeeClientTests : IDisposable
{
    // Backup environment variables to restore after tests
    private readonly string? _originalApiKey;
    private readonly string? _originalApiSecret;
    private readonly string? _originalApiUrl;
    private readonly string? _originalEncKey;

    public EnvbeeClientTests()
    {
        _originalApiKey = Environment.GetEnvironmentVariable("ENVBEE_API_KEY");
        _originalApiSecret = Environment.GetEnvironmentVariable("ENVBEE_API_SECRET");
        _originalApiUrl = Environment.GetEnvironmentVariable("ENVBEE_API_URL");
        _originalEncKey = Environment.GetEnvironmentVariable("ENVBEE_ENC_KEY");
    }

    public void Dispose()
    {
        // Restore environment variables
        SetOrClearEnv("ENVBEE_API_KEY", _originalApiKey);
        SetOrClearEnv("ENVBEE_API_SECRET", _originalApiSecret);
        SetOrClearEnv("ENVBEE_API_URL", _originalApiUrl);
        SetOrClearEnv("ENVBEE_ENC_KEY", _originalEncKey);
    }

    private void SetOrClearEnv(string name, string? value)
    {
        if (value is null)
            Environment.SetEnvironmentVariable(name, null);
        else
            Environment.SetEnvironmentVariable(name, value);
    }

    [Fact]
    public void Init_WithAllParameters_Succeeds()
    {
        var client = new EnvbeeClient(
            apiKey: "key123",
            apiSecret: "secret123",
            encKey: "encryption-key",
            baseUrl: "https://custom.url"
        );

        Assert.NotNull(client);
    }

    [Fact]
    public void Init_FromEnvironmentVariables_Succeeds()
    {
        Environment.SetEnvironmentVariable("ENVBEE_API_KEY", "key-env");
        Environment.SetEnvironmentVariable("ENVBEE_API_SECRET", "secret-env");
        Environment.SetEnvironmentVariable("ENVBEE_API_URL", "https://from.env");
        Environment.SetEnvironmentVariable("ENVBEE_ENC_KEY", "enc-env");

        var client = new EnvbeeClient();

        Assert.NotNull(client);
    }

    [Fact]
    public void MissingApiKey_ThrowsArgumentException()
    {
        Environment.SetEnvironmentVariable("ENVBEE_API_SECRET", "secret-env");
        Environment.SetEnvironmentVariable("ENVBEE_API_KEY", null);

        var ex = Assert.Throws<ArgumentException>(() => new EnvbeeClient());

        Assert.Contains("API key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingApiSecret_ThrowsArgumentException()
    {
        Environment.SetEnvironmentVariable("ENVBEE_API_KEY", "key-env");
        Environment.SetEnvironmentVariable("ENVBEE_API_SECRET", null);

        var ex = Assert.Throws<ArgumentException>(() => new EnvbeeClient());

        Assert.Contains("API secret", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParameterTakesPriorityOverEnvironment()
    {
        Environment.SetEnvironmentVariable("ENVBEE_API_KEY", "env-key");
        Environment.SetEnvironmentVariable("ENVBEE_API_SECRET", "env-secret");

        var client = new EnvbeeClient(
            apiKey: "param-key",
            apiSecret: "param-secret"
        );

        Assert.NotNull(client);
    }

    [Fact]
    public void Init_WithoutEncryptionKey_Succeeds()
    {
        var client = new EnvbeeClient(
            apiKey: "key123",
            apiSecret: "secret123"
        // no encryption key
        );

        Assert.NotNull(client);
    }
}
