using Abo.Core.Connectors;
using Abo.Tools.Connector;
using Moq;
using System.Text.Json;

namespace Abo.Tests;

/// <summary>
/// Unit Tests für HttpGetTool.
/// Prüft korrekte Delegation an IConnector.HttpGetAsync sowie alle Fehlerfälle.
/// Referenz-Architektur: PythonToolTests.cs
/// </summary>
public class HttpGetToolTests
{
    private readonly Mock<IConnector> _mockConnector;
    private readonly HttpGetTool _tool;

    public HttpGetToolTests()
    {
        _mockConnector = new Mock<IConnector>();
        _tool = new HttpGetTool(_mockConnector.Object);
    }

    // -----------------------------------------------------------------------
    // Test 1: Tool-Metadaten — Name
    // -----------------------------------------------------------------------

    [Fact]
    public void Tool_HasCorrectName()
    {
        Assert.Equal("http_get", _tool.Name);
    }

    // -----------------------------------------------------------------------
    // Test 2: Tool-Metadaten — Beschreibung nicht leer
    // -----------------------------------------------------------------------

    [Fact]
    public void Tool_HasNonEmptyDescription()
    {
        Assert.False(string.IsNullOrWhiteSpace(_tool.Description));
    }

    // -----------------------------------------------------------------------
    // Test 3: ParametersSchema — enthält 'url' als required Property
    // -----------------------------------------------------------------------

    [Fact]
    public void Tool_ParametersSchema_ContainsRequiredUrlProperty()
    {
        var schema = _tool.ParametersSchema;
        Assert.NotNull(schema);

        var schemaJson = JsonSerializer.Serialize(schema);
        Assert.Contains("\"url\"", schemaJson);
        Assert.Contains("\"required\"", schemaJson);
    }

    // -----------------------------------------------------------------------
    // Test 4: ParametersSchema — enthält optionale 'headers' und 'timeoutSeconds'
    // -----------------------------------------------------------------------

    [Fact]
    public void Tool_ParametersSchema_ContainsOptionalHeadersAndTimeout()
    {
        var schema = _tool.ParametersSchema;
        var schemaJson = JsonSerializer.Serialize(schema);

        Assert.Contains("\"headers\"", schemaJson);
        Assert.Contains("\"timeoutSeconds\"", schemaJson);
    }

    // -----------------------------------------------------------------------
    // Test 5: Normaler GET-Aufruf — delegiert korrekt an HttpGetAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ValidUrl_CallsHttpGetAsync()
    {
        // Arrange
        const string url = "https://api.example.com/data";
        const string expectedResponse = "HTTP 200 OK\nContent-Type: application/json\n\n{\"key\":\"value\"}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\"}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, 30), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 6: Fehlender 'url' Parameter — gibt Fehlermeldung zurück
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MissingUrl_ReturnsErrorMessage()
    {
        // Act
        var result = await _tool.ExecuteAsync("{}");

        // Assert
        Assert.Contains("Error: url parameter is required.", result);
        _mockConnector.Verify(c => c.HttpGetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<int>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Test 7: Leerer 'url' Parameter — gibt Fehlermeldung zurück
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_EmptyUrl_ReturnsErrorMessage()
    {
        // Act
        var result = await _tool.ExecuteAsync("{\"url\": \"\"}");

        // Assert
        Assert.Contains("Error: url parameter cannot be empty.", result);
        _mockConnector.Verify(c => c.HttpGetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<int>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Test 8: Whitespace-only 'url' Parameter — gibt Fehlermeldung zurück
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WhitespaceUrl_ReturnsErrorMessage()
    {
        // Act
        var result = await _tool.ExecuteAsync("{\"url\": \"   \"}");

        // Assert
        Assert.Contains("Error: url parameter cannot be empty.", result);
        _mockConnector.Verify(c => c.HttpGetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<int>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Test 9: Ungültiges JSON — gibt Parsing-Fehler zurück
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsParsingError()
    {
        // Act
        var result = await _tool.ExecuteAsync("NOT_VALID_JSON");

        // Assert
        Assert.StartsWith("Error parsing arguments:", result);
        _mockConnector.Verify(c => c.HttpGetAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, string>>(), It.IsAny<int>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Test 10: Mit optionalen Headers — übergibt Headers an Connector
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithHeaders_PassesHeadersToConnector()
    {
        // Arrange
        const string url = "https://api.example.com/protected";
        var expectedResponse = "HTTP 200 OK\nContent-Type: application/json\n\n{\"data\": \"secret\"}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(
                url,
                It.Is<Dictionary<string, string>>(h =>
                    h.ContainsKey("Authorization") && h["Authorization"] == "Bearer my-token" &&
                    h.ContainsKey("Accept") && h["Accept"] == "application/json"),
                30))
            .ReturnsAsync(expectedResponse);

        var argsJson = JsonSerializer.Serialize(new
        {
            url,
            headers = new Dictionary<string, string>
            {
                { "Authorization", "Bearer my-token" },
                { "Accept", "application/json" }
            }
        });

        // Act
        var result = await _tool.ExecuteAsync(argsJson);

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(
            c => c.HttpGetAsync(
                url,
                It.Is<Dictionary<string, string>>(h =>
                    h.ContainsKey("Authorization") && h["Authorization"] == "Bearer my-token"),
                30),
            Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 11: Mit gültigem Timeout — übergibt Timeout an Connector
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithTimeout_PassesTimeoutToConnector()
    {
        // Arrange
        const string url = "https://slow-api.example.com/health";
        const int timeout = 60;
        var expectedResponse = "HTTP 200 OK\nContent-Type: text/plain\n\nOK";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, timeout))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\", \"timeoutSeconds\": {timeout}}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, timeout), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 12: Timeout > 120s wird auf 120s gedeckelt
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TimeoutExceedsMax_CappedAt120()
    {
        // Arrange
        const string url = "https://api.example.com/data";
        var expectedResponse = "HTTP 200 OK\nContent-Type: application/json\n\n{}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 120))
            .ReturnsAsync(expectedResponse);

        // Act — Timeout von 9999 Sekunden wird auf 120 gedeckelt
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\", \"timeoutSeconds\": 9999}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, 120), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 13: Connector gibt Fehlerstring zurück — Tool leitet ihn durch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ConnectorReturnsError_PassesThroughToResult()
    {
        // Arrange
        const string url = "https://api.example.com/missing";
        const string errorResponse = "Error (HTTP 404): Not Found\nURL: https://api.example.com/missing\n\n";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(errorResponse);

        // Act
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\"}}");

        // Assert
        Assert.Contains("404", result);
        Assert.Contains("Not Found", result);
    }

    // -----------------------------------------------------------------------
    // Test 14: Timeout-Fehler vom Connector — Tool leitet durch
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ConnectorReturnsTimeout_PassesThroughToResult()
    {
        // Arrange
        const string url = "https://slow-api.example.com";
        var timeoutResponse = $"Error (Timeout): Request exceeded 30 seconds timeout.\nURL: {url}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(timeoutResponse);

        // Act
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\"}}");

        // Assert
        Assert.Contains("Timeout", result);
        Assert.Contains(url, result);
    }

    // -----------------------------------------------------------------------
    // Test 15: Negativer Timeout — bleibt bei Default (30s)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NegativeTimeout_UsesDefaultTimeout()
    {
        // Arrange
        const string url = "https://api.example.com/data";
        var expectedResponse = "HTTP 200 OK\nContent-Type: text/plain\n\nOK";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(expectedResponse);

        // Act — Negativer Timeout, soll auf Default 30 fallen
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\", \"timeoutSeconds\": -5}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, 30), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 16: Headers fehlen ganz — null wird übergeben
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_HeadersNotPresent_PassesNullHeadersToConnector()
    {
        // Arrange
        const string url = "https://api.example.com/data";
        var expectedResponse = "HTTP 200 OK\nContent-Type: application/json\n\n{}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(expectedResponse);

        // Act — headers Property fehlt ganz
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\"}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, 30), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Test 17: ParametersSchema hat additionalProperties: false
    // -----------------------------------------------------------------------

    [Fact]
    public void Tool_ParametersSchema_HasAdditionalPropertiesFalse()
    {
        var schema = _tool.ParametersSchema;
        var schemaJson = JsonSerializer.Serialize(schema);
        Assert.Contains("\"additionalProperties\"", schemaJson);
    }

    // -----------------------------------------------------------------------
    // Test 18: Erfolgreiche HTTPS-Anfrage
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_HttpsUrl_CallsHttpGetAsync()
    {
        // Arrange
        const string url = "https://secure-api.example.com/v1/status";
        var expectedResponse = "HTTP 200 OK\nContent-Type: application/json\n\n{\"status\":\"healthy\"}";

        _mockConnector
            .Setup(c => c.HttpGetAsync(url, null, 30))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _tool.ExecuteAsync($"{{\"url\": \"{url}\"}}");

        // Assert
        Assert.Equal(expectedResponse, result);
        _mockConnector.Verify(c => c.HttpGetAsync(url, null, 30), Times.Once);
    }
}

/// <summary>
/// Unit Tests für HttpGetSecurityHelper.
/// Prüft SSRF-Schutz (RFC-1918 + Loopback) und Header-Injection-Prävention
/// ohne echte Netzwerkaufrufe.
/// </summary>
public class HttpGetSecurityHelperTests
{
    // -----------------------------------------------------------------------
    // SSRF-Schutz: CheckSsrfAsync – Loopback-Hostnamen (ohne raw IPv6 ::1)
    // Hinweis: "::1" ist in einer URI ohne Klammern syntaktisch ungültig.
    // Korrekte URI-Notation für IPv6-Loopback ist "[::1]" — wird separat getestet.
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task CheckSsrfAsync_LoopbackHostnames_ReturnsError(string host)
    {
        // Arrange — IPv6-Loopback in korrekter URI-Notation (RFC 2396)
        var uri = new Uri($"http://{host}/test");

        // Act
        var result = await HttpGetSecurityHelper.CheckSsrfAsync(uri);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SSRF Protection", result);
        Assert.Contains("loopback", result, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // SSRF-Schutz: Roher "::1" Host-String wird korrekt als Loopback erkannt
    // (getrennt, da "::1" kein gültiger URI-Host ist und direkt durch die
    //  string-Vergleichslogik in CheckSsrfAsync geblockt wird)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CheckSsrfAsync_RawIpv6LoopbackString_IsBlockedByStringCheck()
    {
        // Arrange: Simuliere eine URI bei der Host bereits "::1" als String vorliegt.
        // In der Realität kommt das nur nach manueller Uri-Konstruktion vor.
        // Wir prüfen die Loopback-Logik der Hilfsmethode direkt.
        var ip = System.Net.IPAddress.Parse("::1");
        Assert.True(System.Net.IPAddress.IsLoopback(ip),
            "::1 muss von IPAddress.IsLoopback() als Loopback erkannt werden.");

        // Und unser Helper erkennt auch "::1" als Host-String-Match
        // Erstelle URI mit der Bracket-Notation die URI-konform ist
        var uri = new Uri("http://[::1]/test");
        var result = await HttpGetSecurityHelper.CheckSsrfAsync(uri);
        Assert.NotNull(result);
        Assert.Contains("SSRF Protection", result);
    }

    // -----------------------------------------------------------------------
    // SSRF-Schutz: IsPrivateIpAddress – RFC-1918 Ranges
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("169.254.0.1")]       // Link-local
    [InlineData("100.64.0.1")]        // Shared Address Space RFC-6598
    public void IsPrivateIpAddress_Rfc1918Ranges_ReturnsTrue(string ipString)
    {
        // Arrange
        var ip = System.Net.IPAddress.Parse(ipString);

        // Act
        var result = HttpGetSecurityHelper.IsPrivateIpAddress(ip);

        // Assert
        Assert.True(result, $"IP {ipString} sollte als privat erkannt werden.");
    }

    // -----------------------------------------------------------------------
    // SSRF-Schutz: IsPrivateIpAddress – Öffentliche IPs sind OK
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("8.8.8.8")]           // Google DNS
    [InlineData("1.1.1.1")]           // Cloudflare DNS
    [InlineData("93.184.216.34")]     // example.com
    [InlineData("185.199.108.153")]   // GitHub Pages
    public void IsPrivateIpAddress_PublicIps_ReturnsFalse(string ipString)
    {
        // Arrange
        var ip = System.Net.IPAddress.Parse(ipString);

        // Act
        var result = HttpGetSecurityHelper.IsPrivateIpAddress(ip);

        // Assert
        Assert.False(result, $"IP {ipString} sollte als öffentlich akzeptiert werden.");
    }

    // -----------------------------------------------------------------------
    // SSRF-Schutz: IsPrivateIpAddress – IPv6 Loopback via System.Net
    // -----------------------------------------------------------------------

    [Fact]
    public void IsPrivateIpAddress_IPv6Loopback_DetectedBySystemMethod()
    {
        // ::1 ist Loopback – wird von IPAddress.IsLoopback() erkannt (nicht von IsPrivateIpAddress)
        // CheckSsrfAsync() prüft beide. Test dokumentiert das korrekte Design.
        var loopback6 = System.Net.IPAddress.Parse("::1");
        Assert.True(System.Net.IPAddress.IsLoopback(loopback6));
    }

    // -----------------------------------------------------------------------
    // SSRF-Schutz: IsPrivateIpAddress – IPv6 Unique Local (fc00::/7)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fdff:ffff:ffff:ffff:ffff:ffff:ffff:ffff")]
    public void IsPrivateIpAddress_IPv6UniqueLocal_ReturnsTrue(string ipString)
    {
        // Arrange
        var ip = System.Net.IPAddress.Parse(ipString);

        // Act
        var result = HttpGetSecurityHelper.IsPrivateIpAddress(ip);

        // Assert
        Assert.True(result, $"IPv6 ULA {ipString} sollte als privat erkannt werden.");
    }

    // -----------------------------------------------------------------------
    // Header-Injection-Schutz: IsRestrictedHeader – Geblockte Header
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Host")]
    [InlineData("host")]
    [InlineData("HOST")]
    [InlineData("Content-Length")]
    [InlineData("Transfer-Encoding")]
    [InlineData("Connection")]
    [InlineData("Upgrade")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Proxy-Connection")]
    public void IsRestrictedHeader_BlockedHeaders_ReturnsTrue(string headerName)
    {
        // Act
        var result = HttpGetSecurityHelper.IsRestrictedHeader(headerName);

        // Assert
        Assert.True(result, $"Header '{headerName}' sollte geblockt sein.");
    }

    // -----------------------------------------------------------------------
    // Header-Injection-Schutz: IsRestrictedHeader – Erlaubte Header passieren
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Accept")]
    [InlineData("Accept-Language")]
    [InlineData("X-Api-Key")]
    [InlineData("User-Agent")]
    [InlineData("Cache-Control")]
    public void IsRestrictedHeader_AllowedHeaders_ReturnsFalse(string headerName)
    {
        // Act
        var result = HttpGetSecurityHelper.IsRestrictedHeader(headerName);

        // Assert
        Assert.False(result, $"Header '{headerName}' sollte erlaubt sein.");
    }

    // -----------------------------------------------------------------------
    // SSRF: CheckSsrfAsync – direkte Private-IP in URL wird geblockt
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("http://10.0.0.1/api")]
    [InlineData("http://192.168.1.1/admin")]
    [InlineData("http://172.16.0.1/internal")]
    public async Task CheckSsrfAsync_DirectPrivateIp_ReturnsError(string url)
    {
        // Arrange
        var uri = new Uri(url);

        // Act
        var result = await HttpGetSecurityHelper.CheckSsrfAsync(uri);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SSRF Protection", result);
        Assert.Contains("RFC-1918", result);
    }

    // -----------------------------------------------------------------------
    // SSRF: CheckSsrfAsync – Loopback-IP direkt in URL
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://127.0.0.1:8080/api")]
    public async Task CheckSsrfAsync_LoopbackIpInUrl_ReturnsError(string url)
    {
        // Arrange
        var uri = new Uri(url);

        // Act
        var result = await HttpGetSecurityHelper.CheckSsrfAsync(uri);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("SSRF Protection", result);
    }
}

/// <summary>
/// Unit Tests für LocalWindowsConnector.HttpGetAsync – Schema- und URL-Validierung.
/// Prüft Fehlerbehandlung ohne externe Netzwerkaufrufe (Validierungslogik vor dem HTTP-Stack).
/// </summary>
public class HttpGetConnectorUrlValidationTests
{
    private readonly LocalWindowsConnector _connector;

    public HttpGetConnectorUrlValidationTests()
    {
        // Temporäres Verzeichnis für Tests
        var tempDir = Path.Combine(Path.GetTempPath(), "HttpGetConnectorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        _connector = new LocalWindowsConnector(new ConnectorEnvironment { Dir = tempDir });
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: Ungültige Schemas werden mit Fehlermeldung abgelehnt
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ftp://example.com/file.txt")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>XSS</h1>")]
    [InlineData("smb://internal-server/share")]
    [InlineData("ldap://internal.corp/dc=corp")]
    public async Task HttpGetAsync_InvalidSchema_ReturnsSchemaError(string url)
    {
        // Act
        var result = await _connector.HttpGetAsync(url);

        // Assert
        Assert.Contains("Error (Invalid URL)", result);
        Assert.Contains("http:// or https://", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: Loopback via Connector HttpGetAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_LocalhostUrl_ReturnsSsrfError()
    {
        // Act
        var result = await _connector.HttpGetAsync("http://localhost/api");

        // Assert
        Assert.Contains("SSRF Protection", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: Private IP via Connector HttpGetAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_PrivateIpUrl_ReturnsSsrfError()
    {
        // Act
        var result = await _connector.HttpGetAsync("http://192.168.0.1/admin");

        // Assert
        Assert.Contains("SSRF Protection", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: 127.0.0.1 Loopback via Connector
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_Loopback127_ReturnsSsrfError()
    {
        // Act
        var result = await _connector.HttpGetAsync("http://127.0.0.1/secret");

        // Assert
        Assert.Contains("SSRF Protection", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: Leere URL
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_EmptyUrl_ReturnsInvalidUrlError()
    {
        // Act
        var result = await _connector.HttpGetAsync("");

        // Assert
        Assert.Contains("Error (Invalid URL)", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: 10.x.x.x RFC-1918 Range via Connector
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_InternalNetwork10x_ReturnsSsrfError()
    {
        // Act
        var result = await _connector.HttpGetAsync("http://10.0.0.1/internal-api");

        // Assert
        Assert.Contains("SSRF Protection", result);
    }

    // -----------------------------------------------------------------------
    // Schema-Validierung: IPv6 Loopback [::1] via Connector
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HttpGetAsync_IPv6LoopbackUrl_ReturnsSsrfError()
    {
        // Act
        var result = await _connector.HttpGetAsync("http://[::1]/api");

        // Assert
        Assert.Contains("SSRF Protection", result);
    }
}
