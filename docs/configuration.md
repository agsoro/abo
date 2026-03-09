# Configuration and Backend Agnosticism

ABO is designed to be completely agnostic to the underlying AI backend. You can seamlessly switch between local inference servers, secure private cloud instances, or managed gateway providers.

## appsettings.json

Configuration is managed via standard .NET `appsettings.json`. The AI backend is treated as a simple service endpoint.

```json
{
  "Config": {
    "ApiEndpoint": "https://your-internal-ai-gateway/v1",
    "ModelName": "custom-pm-model-v1",
    "TimeoutSeconds": 30
  }
}
```

## Setup Instructions
1. Update `ApiEndpoint` to point to your desired REST-compatible AI model API.
2. Set the `ModelName` to match the model you wish to use for inference.
3. Ensure the infrastructure hosting ABO has network access to the `ApiEndpoint`. 
No further SDK configuration is necessary, as ABO interacts using standard `HttpClient`.

---

## 🔐 API Access & Secret Management

ABO is designed to integrate with multiple internal and external systems (e.g., XpectoLive Backoffice, Mattermost). To maintain security and follow .NET best practices, ABO relies on the **.NET Core Configuration Pipeline**.

### Where are Secrets Stored?
Secrets should **never** be hardcoded or committed to version control. ABO supports the following standard secure storage mechanisms:

1. **Local Development (User Secrets)**
   * During development, use the [.NET Secret Manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) tool (`dotnet user-secrets`).
   * Example: `dotnet user-secrets set "Integrations:XpectoLive:ApiKey" "your-dev-api-key"`

2. **Production / Deployment**
   * **Environment Variables**: Best for containerized deployments (Docker/Kubernetes).
   * **Azure Key Vault / AWS Secrets Manager**: For enterprise production environments, plug these directly into the `IConfiguration` builder at startup.

### Structuring Integrations

Your configuration should cleanly separate different services. Here is an example of how `appsettings.json` (or your secure environment variables) should be structured:

```json
{
  "Config": {
    "ApiEndpoint": "https://openrouter.ai/api/v1",
    "ModelName": "anthropic/claude-3-haiku",
    "TimeoutSeconds": 30
  },
  "Integrations": {
    "XpectoLive": {
      "BaseUrl": "https://backoffice.xpectolive.com/api",
      "ApiKey": "SECRET_STORED_IN_VAULT_OR_ENV"
    },
    "Mattermost": {
      "WebhookUrl": "SECRET_STORED_IN_VAULT_OR_ENV",
      "BotToken": "SECRET_STORED_IN_VAULT_OR_ENV"
    }
  }
}
```

### Accessing APIs in Tools
When building a custom tool (e.g., in `/tools/GetXpectoLiveTickets.cs`), you use standard **Dependency Injection (DI)** to pass typed settings or configured `HttpClient` instances into the tool's constructor:

```csharp
public class GetXpectoLiveTickets : IAboTool
{
    private readonly HttpClient _httpClient;
    
    // The DI container injects a pre-configured HttpClient with the correct BaseUrl and Auth headers
    public GetXpectoLiveTickets(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("XpectoLiveClient");
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        // ... execute REST call to XpectoLive APIs ...
    }
}
```
