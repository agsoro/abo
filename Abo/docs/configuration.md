# Configuration and Backend Agnosticism

ABO is designed to be completely independent of the AI backend in use. It can seamlessly switch between local inference servers, secure private cloud instances, or managed gateway providers.

## appsettings.json

Configuration is handled via the standard .NET file `appsettings.json`. The AI backend is treated as a simple service endpoint.

```json
{
  "Config": {
    "ApiEndpoint": "https://your-internal-ai-gateway/v1",
    "ModelName": "custom-pm-model-v1",
    "CapableModelName": "anthropic/claude-3-5-sonnet",
    "DefaultLanguage": "de-de",
    "TimeoutSeconds": 30
  }
}
```

### All Configuration Keys (`Config` Section)

| Key | Type | Required | Description |
|---|---|---|---|
| `ApiEndpoint` | `string` | ✅ | The base URL of the REST-compatible AI model endpoint (OpenAI-compatible). |
| `ModelName` | `string` | ✅ | The default model name for most agents (e.g. `anthropic/claude-3-haiku`). |
| `CapableModelName` | `string` | ⚠️ Recommended | A more powerful model for complex agents (`PmoAgent`, `EmployeeAgent`). If not set, `ModelName` is used as fallback. |
| `DefaultLanguage` | `string` | ⚠️ Recommended | Default language for all agent responses (e.g. `de-de`, `en-us`). Used in system prompts. |
| `TimeoutSeconds` | `int` | ✅ | Timeout in seconds for HTTP requests to the AI endpoint. |

### Setup Steps

1. Set `ApiEndpoint` to the desired REST-compatible AI model API endpoint.
2. Set `ModelName` to the desired default model.
3. Optional: Configure `CapableModelName` for more demanding agents (PMO, Employee).
4. Optional: Set `DefaultLanguage` to the desired output language (default: `de-de`).
5. Ensure the ABO infrastructure has network access to the `ApiEndpoint`.

No additional SDK configuration is needed since ABO exclusively uses the standard `HttpClient`.

---

## 🔐 API Access & Secret Management

ABO is designed to interact with multiple internal and external systems (e.g. XpectoLive Backoffice, Mattermost). To ensure security and follow .NET best practices, ABO uses the **.NET Core configuration pipeline**.

### Where Are Secrets Stored?

Secrets should **never** be stored in plain text in `appsettings.json` or checked into version control. ABO supports the following secure storage mechanisms:

1. **Local Development (User Secrets)**
   - During development, use the [.NET Secret Manager](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) tool (`dotnet user-secrets`).
   - Example: `dotnet user-secrets set "Integrations:XpectoLive:ApiKey" "your-dev-api-key"`

2. **Production / Deployment**
   - **Environment Variables**: Ideal for containerized deployments (Docker/Kubernetes).
   - **Azure Key Vault / AWS Secrets Manager**: For enterprise production environments, integrate directly into the `IConfiguration` builder at startup.

### Structuring Integrations

Configuration should cleanly separate different services. Example structure for `appsettings.json` (or environment variables):

```json
{
  "Config": {
    "ApiEndpoint": "https://openrouter.ai/api/v1",
    "ModelName": "anthropic/claude-3-haiku",
    "CapableModelName": "anthropic/claude-3-5-sonnet",
    "DefaultLanguage": "de-de",
    "TimeoutSeconds": 30
  },
  "Integrations": {
    "XpectoLive": {
      "BaseUrl": "https://backoffice.xpectolive.com/api",
      "ApiKey": "SECRET_IN_VAULT_OR_ENV"
    },
    "Mattermost": {
      "BaseUrl": "https://work.xpecto.com/chat",
      "BotToken": "SECRET_IN_VAULT_OR_ENV"
    }
  }
}
```

### All Integration Keys

| Key | Description |
|---|---|
| `Integrations:XpectoLive:BaseUrl` | Base URL of the XpectoLive Backoffice REST API |
| `Integrations:XpectoLive:ApiKey` | API key for XpectoLive (Secret!) |
| `Integrations:Mattermost:BaseUrl` | Base URL of the Mattermost instance |
| `Integrations:Mattermost:BotToken` | Bot token for the Mattermost bot (Secret!) |

---

### Using APIs in Tools

When creating a custom tool (e.g. in `/Tools/GetXpectoLiveTickets.cs`), use **Dependency Injection (DI)** to receive typed settings or pre-configured `HttpClient` instances:

```csharp
public class GetXpectoLiveTickets : IAboTool
{
    private readonly HttpClient _httpClient;
    
    // The DI container injects a pre-configured HttpClient with correct BaseUrl and auth headers
    public GetXpectoLiveTickets(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("XpectoLiveClient");
    }

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        // ... REST call to XpectoLive APIs ...
    }
}
```

---

## 🐛 Debugging: LLM Traffic Log

ABO automatically logs all AI API requests and responses to a JSONL file:

- **Path**: `Data/llm_traffic.jsonl`
- **Format**: One JSON object per line (JSONL), each with a timestamp, request, and response.
- **Usage**: Ideal for error analysis when unexpected agent behavior or API issues occur.
- **Note**: This file can grow very large under heavy usage. It should **not** be checked into version control (a `.gitignore` entry is recommended).
