using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

/// <summary>
/// Connector-Tool das HTTP GET Requests an externe URLs sendet.
/// Implementiert SSRF-Schutz, Response-Truncation (100 KB) und Timeout-Handling.
/// Folgt dem Architekturmuster der bestehenden Connector-Tools (IAboTool + IConnector).
/// </summary>
public class HttpGetTool : IAboTool
{
    private readonly IConnector _connector;

    /// <summary>Maximaler Timeout in Sekunden (wird intern auf diesen Wert gedeckelt).</summary>
    private const int MaxTimeoutSeconds = 120;

    public HttpGetTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "http_get";

    public string Description =>
        "Sends an HTTP GET request to the specified URL and returns the HTTP status code and response body. " +
        "Use this to query external APIs, health endpoints, or fetch remote data. " +
        "Response is limited to 100KB. Only http:// and https:// URLs are allowed.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            url = new
            {
                type = "string",
                description = "The full URL to send the GET request to (must start with http:// or https://)."
            },
            headers = new
            {
                type = "object",
                description = "Optional HTTP headers to include in the request (e.g., { \"Authorization\": \"Bearer token\", \"Accept\": \"application/json\" }).",
                additionalProperties = new { type = "string" }
            },
            timeoutSeconds = new
            {
                type = "integer",
                description = "Request timeout in seconds. Defaults to 30. Maximum: 120."
            }
        },
        required = new[] { "url" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            // FA-08 – url Parameter ist Pflicht
            if (!root.TryGetProperty("url", out var urlElement))
                return "Error: url parameter is required.";

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
                return "Error: url parameter cannot be empty.";

            // FA-04 – Optionale Headers parsen
            Dictionary<string, string>? headers = null;
            if (root.TryGetProperty("headers", out var headersElement)
                && headersElement.ValueKind == JsonValueKind.Object)
            {
                headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in headersElement.EnumerateObject())
                {
                    var headerValue = prop.Value.GetString();
                    if (headerValue != null)
                        headers[prop.Name] = headerValue;
                }
            }

            // FA-05 – Optionaler Timeout (Standard: 30s, gedeckelt auf 120s)
            int timeout = 30;
            if (root.TryGetProperty("timeoutSeconds", out var timeoutEl)
                && timeoutEl.TryGetInt32(out var parsedTimeout)
                && parsedTimeout > 0)
            {
                timeout = Math.Min(parsedTimeout, MaxTimeoutSeconds);
            }

            return await _connector.HttpGetAsync(url, headers, timeout);
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
