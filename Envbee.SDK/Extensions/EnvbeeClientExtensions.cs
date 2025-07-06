using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Envbee.SDK.Extensions;

/// <summary>
/// IServiceCollection helper that wires up <see cref="EnvbeeClient"/> in any
/// ASP.NET‑Core or generic‑host application.  The extension follows the same
/// pattern used by most official SDKs: a single call in Program.cs and the
/// client is ready for DI.
/// </summary>
public static class EnvbeeClientExtensions
{
    /// <summary>
    /// Registers an <see cref="EnvbeeClient"/> as <c>Singleton</c>.
    /// Example usage:
    ///
    /// <code>
    /// builder.Services.AddEnvbee(cfg => cfg
    ///     .WithCredentials(myKey, mySecret)
    ///     .WithEncryptionKey(myEncKey));
    /// </code>
    /// </summary>
    public static IServiceCollection AddEnvbee(
        this IServiceCollection services,
        Action<EnvbeeClientBuilder> configureBuilder)
    {
        if (configureBuilder is null)
            throw new ArgumentNullException(nameof(configureBuilder));

        // Register a named HttpClient so callers can attach Polly, logging, etc.
        services.AddHttpClient("Envbee");

        services.AddSingleton(provider =>
        {
            var builder = EnvbeeClientBuilder.Create();

            // If the consumer configured the default HttpClient for "Envbee",
            // reuse its primary handler so sockets, logging and Polly policies
            // are shared with the SDK.
            var factory = provider.GetRequiredService<IHttpClientFactory>();
            var client = factory.CreateClient("Envbee");

            var handler = client.GetPrimaryHandler();      // helper below
            if (handler is not null)
                builder.WithHttpMessageHandler(handler);

            configureBuilder(builder);
            return builder.Build();
        });

        return services;
    }

    /// <summary>
    /// Uses reflection to extract the primary handler from an HttpClient
    /// created by IHttpClientFactory.  This keeps EnvbeeClient free from
    /// IHttpClientFactory while still allowing shared handler stacks.
    /// </summary>
    private static HttpMessageHandler? GetPrimaryHandler(this HttpClient client)
        => client.GetType()
                 .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)?
                 .GetValue(client) as HttpMessageHandler;
}
