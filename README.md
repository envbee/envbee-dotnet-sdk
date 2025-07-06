# Envbee .NET SDK

[![NuGet](https://img.shields.io/nuget/v/Envbee.SDK.svg)](https://www.nuget.org/packages/Envbee.SDK)

Envbee SDK is a .NET client for interacting with the Envbee API ([https://envbee.dev](https://envbee.dev)).
This SDK allows you to securely fetch variables, handle local caching, and optionally decrypt encrypted values using AES-GCM.

## Table of Contents

- [Installation](#installation)
- [Usage](#usage)
- [Environment Variables](#environment-variables)
- [Methods](#methods)
- [Encryption](#encryption)
- [Logging](#logging)
- [Caching](#caching)
- [Dependency Injection](#dependency-injection)
- [API Documentation](#api-documentation)
- [License](#license)

## Installation

You can install the Envbee SDK from [NuGet](https://www.nuget.org/packages/Envbee.SDK) using the .NET CLI:

```bash
dotnet add package Envbee.SDK
```

Or using the NuGet Package Manager in Visual Studio:

```bash
Install-Package Envbee.SDK
```

## Usage

You can instantiate the `EnvbeeClient` class directly with parameters, or configure it using the fluent builder pattern.

```csharp
using Envbee.SDK;

var client = new EnvbeeClient(
    apiKey: "your_api_key",
    apiSecret: "your_api_secret",
    encKey: "encryption-key-goes-here" // optional
);

string? value = await client.GetAsync("VariableName");
```

Or using the builder:

```csharp
var client = EnvbeeClientBuilder.Create()
    .WithCredentials("your_api_key", "your_api_secret")
    .WithEncryptionKey("32-byte-encryption-key-goes-here")
    .Build();
```

## Environment Variables

The SDK supports loading credentials and configuration from environment variables:

- `ENVBEE_API_KEY`
- `ENVBEE_API_SECRET`
- `ENVBEE_ENC_KEY`

```bash
export ENVBEE_API_KEY="your_api_key"
export ENVBEE_API_SECRET="your_api_secret"
export ENVBEE_ENC_KEY="encryption-key"
```

Then initialize with:

```csharp
var client = new EnvbeeClient();
```

Explicit parameters take precedence over environment variables.

## Methods

- `Task<object?> GetAsync(string variableName)` — fetches a variable and returns its value as `string`, `decimal`, `int`, `double` or `bool`
- `Task<(IReadOnlyList<JsonElement> Data, Metadata Meta)> GetVariablesAsync(int? offset = null, int? limit = null)` — fetches variable metadata with pagination

## Encryption

Variables may be encrypted using AES-256-GCM. Encrypted values start with prefix `envbee:enc:v1:`.

- If an encrypted variable is fetched and a valid `encKey` is provided, the SDK will decrypt it automatically.
- If decryption fails due to missing or incorrect key, a `DecryptionException` is thrown.
- The key is never sent to the server; all cryptographic operations happen locally.

```csharp
var client = new EnvbeeClient(
    apiKey: "your_api_key",
    apiSecret: "your_api_secret",
    encKey: "encryption-key-goes-here"
);
```

## Logging

The SDK uses `Microsoft.Extensions.Logging`.

## Caching

The SDK caches variables locally to provide fallback data when offline or the API is unreachable. The cache is updated after each successful API call. Local cache stores variables as received from the API, encrypted or plain.

- Encryption key is never stored in cache or sent to API.
- All encryption/decryption happens locally with AES-256-GCM.

## Dependency Injection

In ASP.NET Core or generic host apps, you can register the client via extension:

```csharp
builder.Services.AddEnvbee(cfg => cfg
    .WithCredentials(apiKey, apiSecret)
    .WithEncryptionKey(encKey));
```

This registers the client as a singleton. Internally it shares the default `HttpClient` handler stack.

## API Documentation

For more information on envbee API endpoints and usage, visit the [official API documentation](https://docs.envbee.dev).

## License

This project is licensed under the MIT License. See the LICENSE file for details.
