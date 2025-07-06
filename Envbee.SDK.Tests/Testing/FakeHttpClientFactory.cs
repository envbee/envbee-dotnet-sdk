namespace Envbee.SDK.Tests.Testing;

public sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public FakeHttpClientFactory(HttpMessageHandler handler)
        => _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4) };

    public HttpClient CreateClient(string name) => _client;
}
