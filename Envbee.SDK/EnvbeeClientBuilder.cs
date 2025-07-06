using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Envbee.SDK
{
    /// <summary>
    /// Fluent builder that allows constructing an <see cref="EnvbeeClient"/>
    /// without requiring a DI container. It mirrors the ergonomic pattern
    /// used by MinIO and AWS SDK builders.
    /// </summary>
    public sealed class EnvbeeClientBuilder
    {
        private string? _baseUrl;
        private string? _apiKey;
        private Secret _apiSecret = default;
        private Secret _encKey = default;
        private HttpMessageHandler? _handler;

        private EnvbeeClientBuilder() { }

        /// <summary>
        /// Entry point to start building a client.
        /// </summary>
        public static EnvbeeClientBuilder Create() => new();

        /// <summary>
        /// Overrides the default Envbee API endpoint.
        /// </summary>
        public EnvbeeClientBuilder WithEndpoint(string url)
        {
            _baseUrl = url;
            return this;
        }

        /// <summary>
        /// Sets the API key and secret used for HMAC authentication.
        /// </summary>
        public EnvbeeClientBuilder WithCredentials(string apiKey, Secret apiSecret)
        {
            _apiKey = apiKey;
            _apiSecret = apiSecret;
            return this;
        }

        /// <summary>
        /// Sets the encryption key for decrypting values returned by the API.
        /// </summary>
        public EnvbeeClientBuilder WithEncryptionKey(Secret encKey)
        {
            _encKey = encKey;
            return this;
        }

        /// <summary>
        /// Injects a custom <see cref="HttpMessageHandler"/>.
        /// Useful for unit tests or custom transport pipelines.
        /// </summary>
        public EnvbeeClientBuilder WithHttpMessageHandler(HttpMessageHandler handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <summary>
        /// Builds the configured <see cref="EnvbeeClient"/>.
        /// </summary>
        public EnvbeeClient Build()
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("API key is missing. Call WithCredentials().");
            if (_apiSecret.IsEmpty)
                throw new InvalidOperationException("API secret is missing. Call WithCredentials().");

            var client = new EnvbeeClient(
                apiKey: _apiKey,
                apiSecret: _apiSecret,
                encKey: _encKey,
                baseUrl: _baseUrl);

            // If the user provided a custom handler, swap the static HttpClient via reflection.
            if (_handler is not null)
            {
                if (_handler is not null)
                {
                    var custom = new HttpClient(_handler) { Timeout = TimeSpan.FromSeconds(4) };
                    EnvbeeClient.OverrideHttpClient(custom);
                }
            }

            return client;
        }
    }

    /// <summary>
    /// DI helper extensions so that consumers can register EnvbeeClient with a single call.
    /// </summary>
    public static class EnvbeeServiceCollectionExtensions
    {
        /// <summary>
        /// Registers an <see cref="EnvbeeClient"/> in the service container using the fluent builder.
        /// Example:
        /// <code>
        /// services.AddEnvbee(builder => builder
        ///     .WithCredentials(apiKey, apiSecret)
        ///     .WithEncryptionKey(encKey));
        /// </code>
        /// </summary>
        public static IServiceCollection AddEnvbee(this IServiceCollection services, Action<EnvbeeClientBuilder> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));

            services.AddSingleton(provider =>
            {
                // Optional: allow handler injection via named HttpClient
                var builder = EnvbeeClientBuilder.Create();

                // Provide default logging handler from DI if the user wants to hook delegating handlers.
                if (provider.GetService<IHttpClientFactory>() is IHttpClientFactory factory)
                {
                    var handler = factory.CreateClient("Envbee").GetHttpMessageHandler();
                    if (handler is not null)
                        builder.WithHttpMessageHandler(handler);
                }

                configure(builder);
                return builder.Build();
            });

            return services;
        }

        /// <summary>
        /// Helper to get the primary handler from an HttpClient created via factory.
        /// </summary>
        private static HttpMessageHandler? GetHttpMessageHandler(this HttpClient client)
        {
            return (HttpMessageHandler?)client.GetType()
                .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(client);
        }
    }
}
