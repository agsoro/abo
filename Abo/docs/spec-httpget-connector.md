# Technische Spezifikation: `httpget` im abo-Connector

**Dokument-Typ**: Technische Spezifikation (Tech Spec)  
**Issue / Epic**: ABO-XXXX – `httpget`-Tool für den abo-Connector  
**Autor**: Tech Lead  
**Status**: ENTWURF  
**Datum**: 2025-01-XX  
**Version**: 1.0.0  

---

## 1. Zusammenfassung (Executive Summary)

Das ABO-System (Agsoro Bot Orchestrator) besitzt aktuell im Connector-Subsystem Werkzeuge für Dateisystem-Operationen (`read_file`, `write_file`, `list_dir`, etc.) und Shell-Befehle (`git`, `dotnet`, `python`). Für KI-gestützte Agenten fehlt jedoch die Fähigkeit, **aus einem laufenden Projekt heraus strukturierte HTTP-GET-Anfragen** an externe APIs oder interne Services zu senden.

Die vorliegende Spezifikation definiert die Implementierung eines neuen Connector-Tools `httpget`, das nach dem bewährten Architekturmuster der existierenden Connector-Tools umgesetzt wird.

---

## 2. Motivation und Anforderungen

### 2.1 Problem-Statement

SpecialistAgents (z. B. Tech Lead, QA Agent, Developer) benötigen im Kontext ihrer Projektaufgaben die Möglichkeit, externe Datenquellen abzufragen. Beispiel-Use-Cases:

- Abruf von REST-API-Dokumentationen (Swagger/OpenAPI)
- Prüfung von Service-Health-Endpunkten während eines Deployments
- Lesen von Projekt-spezifischen Konfigurationsdaten von einem internen Dienst
- Abfrage von CI/CD-Status-Endpunkten
- Validierung von API-Antworten im Rahmen von QA-Aufgaben

Aktuell müssen Agenten entweder auf Mattermost-Integrationen oder auf das `python`-Tool ausweichen, was umständlich und nicht idiomatisch ist.

### 2.2 Funktionale Anforderungen

| ID | Anforderung | Priorität |
|----|-------------|-----------|
| FA-01 | Das Tool sendet einen HTTP GET Request an eine gegebene URL | MUST |
| FA-02 | Response Body wird als String zurückgegeben | MUST |
| FA-03 | Der HTTP Status Code wird zusammen mit der Response zurückgegeben | MUST |
| FA-04 | Optionale Custom-Headers können als Key-Value-Map mitgegeben werden | SHOULD |
| FA-05 | Ein konfigurierbarer Timeout (Standard: 30 Sekunden) | SHOULD |
| FA-06 | Fehler (Netzwerk, Timeout, nicht-2xx) werden als klare Fehlermeldung zurückgegeben | MUST |
| FA-07 | Response-Größe ist auf ein Maximum begrenzt (Standard: 100 KB) | MUST |
| FA-08 | URL-Validierung vor dem Request | MUST |

### 2.3 Nicht-funktionale Anforderungen

| ID | Anforderung |
|----|-------------|
| NFA-01 | Das Tool folgt **exakt** dem Muster der bestehenden Connector-Tools (`IAboTool` + `IConnector`) |
| NFA-02 | Keine neuen externen NuGet-Pakete; nur `System.Net.Http` (bereits im .NET SDK enthalten) |
| NFA-03 | Unit Tests mit Moq (analog zu `PythonToolTests.cs`) sind zwingend erforderlich |
| NFA-04 | Path-Confinement gilt nicht für HTTP (URLs sind kein Dateipfad), aber URL-Sanitisierung ist Pflicht |
| NFA-05 | Das Tool ist **ausschließlich** nach `checkout_task` verfügbar (wie alle Connector-Tools) |

---

## 3. Architektur-Übersicht

### 3.1 Einordnung in die bestehende Architektur

Das `httpget`-Tool fügt sich in das bestehende Schichtenmodell ein:

```
┌─────────────────────────────────────────────────────┐
│                  LLM (OpenRouter)                    │
│           Tool Call: { "url": "...", ... }           │
└───────────────────────┬─────────────────────────────┘
                        │ JSON Tool Call
                        ▼
┌─────────────────────────────────────────────────────┐
│              SpecialistAgent / Orchestrator          │
│         HandleToolCallAsync("http_get", args)        │
└───────────────────────┬─────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────┐
│            HttpGetTool (IAboTool)                   │
│       /Tools/Connector/HttpGetTool.cs               │
│  - Deserialisiert JSON-Args                          │
│  - Delegiert an IConnector.HttpGetAsync(...)         │
└───────────────────────┬─────────────────────────────┘
                        │
                        ▼
┌─────────────────────────────────────────────────────┐
│         LocalWindowsConnector (IConnector)           │
│  HttpGetAsync(url, headers, timeoutSeconds)          │
│  - URL-Validierung                                   │
│  - HttpClient.GetAsync(url)                          │
│  - Response-Truncation (max 100 KB)                  │
│  - Status-Code-Rückgabe im Result-String             │
└───────────────────────┬─────────────────────────────┘
                        │
                        ▼
                  [Externes HTTP-Ziel]
```

### 3.2 Vergleich mit bestehenden Tools

| Aspekt | PythonTool (Referenz) | HttpGetTool (neu) |
|--------|-----------------------|-------------------|
| Interface | `IAboTool` | `IAboTool` |
| Delegation | `IConnector.RunPythonAsync` | `IConnector.HttpGetAsync` |
| Parameter | `arguments: string` | `url: string`, `headers?: object`, `timeoutSeconds?: int` |
| Rückgabe | Process stdout/stderr | HTTP Status + Response Body |
| Fehlerbehandlung | Exit Code + stderr | HTTP-Status + Exception Message |
| Tests | `PythonToolTests.cs` | `HttpGetToolTests.cs` (neu) |

---

## 4. Schnittstellendefinition

### 4.1 IConnector – neue Methode

```csharp
// Datei: Abo/Core/Connectors/IConnector.cs

public interface IConnector
{
    // ... bestehende Methoden ...
    
    /// <summary>
    /// Führt einen HTTP GET Request an die angegebene URL aus.
    /// </summary>
    /// <param name="url">Die vollständige URL (muss http:// oder https:// beginnen).</param>
    /// <param name="headers">Optionale HTTP-Header als Dictionary.</param>
    /// <param name="timeoutSeconds">Timeout in Sekunden (Standard: 30).</param>
    /// <returns>Formatierter String mit HTTP-Status und Response Body (max 100 KB).</returns>
    Task<string> HttpGetAsync(
        string url, 
        Dictionary<string, string>? headers = null, 
        int timeoutSeconds = 30
    );
}
```

### 4.2 Tool-Definition (JSON Schema für LLM)

```json
{
  "name": "http_get",
  "description": "Sends an HTTP GET request to the specified URL and returns the HTTP status code and response body. Use this to query external APIs, health endpoints, or fetch remote data. Response is limited to 100KB.",
  "parameters": {
    "type": "object",
    "properties": {
      "url": {
        "type": "string",
        "description": "The full URL to send the GET request to (must start with http:// or https://)."
      },
      "headers": {
        "type": "object",
        "description": "Optional HTTP headers to include in the request (e.g., { \"Authorization\": \"Bearer token\", \"Accept\": \"application/json\" }).",
        "additionalProperties": { "type": "string" }
      },
      "timeoutSeconds": {
        "type": "integer",
        "description": "Request timeout in seconds. Defaults to 30. Maximum: 120."
      }
    },
    "required": ["url"],
    "additionalProperties": false
  }
}
```

### 4.3 Rückgabeformat des Tools

Das Tool gibt einen **strukturierten Plaintext-String** zurück (kompatibel mit dem LLM-Kontext):

```
HTTP 200 OK
Content-Type: application/json

{ "key": "value", ... }
```

Im Fehlerfall:
```
Error (HTTP 404): Not Found
URL: https://example.com/api/missing

Error (Timeout): Request exceeded 30 seconds timeout.
URL: https://slow-api.example.com

Error (Invalid URL): URL must start with http:// or https://. Got: ftp://example.com
```

### 4.4 HttpGetTool – Klassen-Skelett

```csharp
// Datei: Abo/Tools/Connector/HttpGetTool.cs

using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class HttpGetTool : IAboTool
{
    private readonly IConnector _connector;

    public HttpGetTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "http_get";
    
    public string Description => 
        "Sends an HTTP GET request to the specified URL and returns the HTTP status code and response body. " +
        "Use this to query external APIs, health endpoints, or fetch remote data. Response is limited to 100KB.";

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
                description = "Optional HTTP headers as key-value pairs.",
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

            if (!root.TryGetProperty("url", out var urlElement))
                return "Error: url parameter is required.";

            var url = urlElement.GetString();
            if (string.IsNullOrWhiteSpace(url))
                return "Error: url parameter cannot be empty.";

            Dictionary<string, string>? headers = null;
            if (root.TryGetProperty("headers", out var headersElement) 
                && headersElement.ValueKind == JsonValueKind.Object)
            {
                headers = new Dictionary<string, string>();
                foreach (var prop in headersElement.EnumerateObject())
                {
                    headers[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }

            int timeout = 30;
            if (root.TryGetProperty("timeoutSeconds", out var timeoutEl) 
                && timeoutEl.TryGetInt32(out var parsedTimeout)
                && parsedTimeout > 0)
            {
                timeout = Math.Min(parsedTimeout, 120); // Cap at 120s
            }

            return await _connector.HttpGetAsync(url, headers, timeout);
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
```

### 4.5 LocalWindowsConnector – HttpGetAsync Implementierung

```csharp
// Erweiterung in: Abo/Core/Connectors/LocalWindowsConnector.cs

private static readonly HttpClient _sharedHttpClient = new HttpClient();
private const int MaxResponseSizeBytes = 100 * 1024; // 100 KB

public async Task<string> HttpGetAsync(
    string url,
    Dictionary<string, string>? headers = null,
    int timeoutSeconds = 30)
{
    // 1. URL-Validierung
    if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
        !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return $"Error (Invalid URL): URL must start with http:// or https://. Got: {url}";
    }

    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return $"Error (Invalid URL): Could not parse URL: {url}";
    }

    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);

        // 2. Optionale Headers hinzufügen
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                // Sicherheits-Check: keine verbotenen System-Header
                if (!IsRestrictedHeader(key))
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }
        }

        // 3. Request ausführen
        using var response = await _sharedHttpClient.SendAsync(
            request, 
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token
        );

        // 4. Content-Type lesen
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "unknown";
        
        // 5. Body lesen (mit Größenbegrenzung)
        var bodyBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
        string body;
        
        if (bodyBytes.Length > MaxResponseSizeBytes)
        {
            body = System.Text.Encoding.UTF8.GetString(bodyBytes, 0, MaxResponseSizeBytes);
            body += $"\n\n[... Response truncated at {MaxResponseSizeBytes / 1024} KB ...]";
        }
        else
        {
            body = System.Text.Encoding.UTF8.GetString(bodyBytes);
        }

        // 6. Fehler-Status behandeln
        if (!response.IsSuccessStatusCode)
        {
            return $"Error (HTTP {(int)response.StatusCode}): {response.ReasonPhrase}\n" +
                   $"URL: {url}\n\n{body}";
        }

        // 7. Erfolgs-Response zurückgeben
        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n" +
               $"Content-Type: {contentType}\n\n{body}";
    }
    catch (OperationCanceledException)
    {
        return $"Error (Timeout): Request exceeded {timeoutSeconds} seconds timeout.\nURL: {url}";
    }
    catch (HttpRequestException ex)
    {
        return $"Error (Network): {ex.Message}\nURL: {url}";
    }
    catch (Exception ex)
    {
        return $"Error: {ex.Message}\nURL: {url}";
    }
}

private static bool IsRestrictedHeader(string headerName)
{
    // Verhindert Injection von sensiblen System-Headern
    var restricted = new[] { "Host", "Content-Length", "Transfer-Encoding", "Connection" };
    return restricted.Any(r => r.Equals(headerName, StringComparison.OrdinalIgnoreCase));
}
```

---

## 5. Sicherheitsbetrachtung

### 5.1 SSRF (Server-Side Request Forgery) – Risiko: MITTEL

Da der Connector auf einem Windows-Server läuft und beliebige URLs abrufen kann, besteht SSRF-Risiko.

**Gegenmaßnahmen**:

```csharp
// In HttpGetAsync – nach Uri.TryCreate:
private static bool IsPrivateOrLoopbackAddress(Uri uri)
{
    // Blockiere Loopback
    if (uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1")
        return true;
    
    // Blockiere interne IP-Ranges (RFC 1918 + link-local)
    try
    {
        var addresses = System.Net.Dns.GetHostAddresses(uri.Host);
        return addresses.Any(a =>
            System.Net.IPAddress.IsLoopback(a) ||
            IsRfc1918Address(a)
        );
    }
    catch { return false; }
}
```

> ⚠️ **Entscheidung Tech Lead**: Für die erste Iteration (v1.0) werden nur `http://` und `https://` Schemas zugelassen. SSRF-Blocklisten für interne IPs werden als **MUST-HAVE für v1.0** definiert, da das Risiko der Exposition interner Services inakzeptabel ist.

### 5.2 Header Injection – Risiko: NIEDRIG

Gegenmaßnahme: `TryAddWithoutValidation` plus explizite Blockliste für System-Header (siehe `IsRestrictedHeader`).

### 5.3 Sensitive Data in Logs – Risiko: NIEDRIG

Authorization-Header mit Bearer-Tokens werden **nicht** ins LLM-Traffic-Log geschrieben (der Response-Body schon). Dies ist bewusstes Design: Der Agent muss die URL und Parameter selbst aus dem Projektkontext ableiten.

---

## 6. Abhängigkeiten

### 6.1 Interne Abhängigkeiten

| Komponente | Änderung | Typ |
|-----------|----------|-----|
| `IConnector` | Neue Methode `HttpGetAsync` | Interface-Erweiterung |
| `LocalWindowsConnector` | Implementierung von `HttpGetAsync` | Klassen-Erweiterung |
| `SpecialistAgent` | Registrierung von `HttpGetTool` in `GetToolDefinitions()` | Klassen-Erweiterung |
| `Program.cs` | Kein Handlungsbedarf (Tools werden über DI als Connector-Tools geliefert) | — |

### 6.2 Externe Abhängigkeiten

| Paket/Library | Bereits vorhanden? | Anmerkung |
|--------------|-------------------|-----------|
| `System.Net.Http.HttpClient` | ✅ Ja (.NET SDK) | Kein neues NuGet-Paket nötig |
| `System.Text.Json` | ✅ Ja | Bereits in allen Tools verwendet |

### 6.3 Test-Abhängigkeiten

| Paket | Bereits vorhanden? | Anmerkung |
|-------|-------------------|-----------|
| `xunit` | ✅ Ja | Siehe `Abo.Tests` |
| `Moq` | ✅ Ja | Siehe `PythonToolTests.cs` |

---

## 7. Risiken und Mitigationen

| Risiko | Eintrittswahrscheinlichkeit | Impact | Mitigation |
|--------|----------------------------|--------|------------|
| SSRF auf interne Services | Mittel | Hoch | IP-Blockliste für RFC-1918-Ranges implementieren |
| Große API-Responses überfluten Kontext | Mittel | Mittel | Hard-Cap auf 100 KB mit Truncation-Hinweis |
| Timeout blockiert Agent-Loop | Niedrig | Mittel | CancellationToken mit 120s Max-Cap |
| Shared `HttpClient` für Connector-Instanzen | Niedrig | Niedrig | `static readonly` HttpClient ist Best Practice (.NET) |
| Verarbeitung von Binary-Content | Niedrig | Niedrig | Encoding-Fehler werden abgefangen; Tool ist für Text/JSON optimiert |
| Missbrauch durch LLM (unbeabsichtigte Requests) | Niedrig | Mittel | Nur nach `checkout_task` verfügbar; URL-Validierung |

---

## 8. Implementierungsplan

### Phase 1: Core Implementation (Aufwand: 3 Story Points – Fibonacci)

```
[ ] IConnector.cs          – HttpGetAsync Methoden-Signatur hinzufügen
[ ] LocalWindowsConnector  – HttpGetAsync implementieren (inkl. SSRF-Schutz)
[ ] HttpGetTool.cs         – Neues Tool anlegen (nach PythonTool-Muster)
[ ] SpecialistAgent.cs     – HttpGetTool in GetToolDefinitions() registrieren
```

### Phase 2: Tests (Aufwand: 2 Story Points)

```
[ ] HttpGetToolTests.cs    – Unit Tests für:
    [ ] Tool_HasCorrectName ("http_get")
    [ ] Tool_HasNonEmptyDescription
    [ ] Tool_ParametersSchema_ContainsRequiredUrlProperty
    [ ] ExecuteAsync_ValidUrl_CallsHttpGetAsync
    [ ] ExecuteAsync_MissingUrl_ReturnsErrorMessage
    [ ] ExecuteAsync_InvalidJson_ReturnsParsingError
    [ ] ExecuteAsync_WithHeaders_PassesHeadersToConnector
    [ ] ExecuteAsync_WithTimeout_PassesTimeoutToConnector
    [ ] ExecuteAsync_ConnectorReturnsError_PassesThroughToResult
    [ ] ExecuteAsync_TimeoutExceedsMax_CapedAt120
```

### Phase 3: Dokumentation (Aufwand: 1 Story Point)

```
[ ] architecture.md        – Connector-Tabelle um http_get erweitern
[ ] tools.md               – http_get Tool-Dokumentation hinzufügen
[ ] Diese Spec-Datei       – Als finale Referenz in Docs/ ablegen
```

**Gesamt-Schätzung: 5 Story Points (Fibonacci) – Confidence: HOCH**

---

## 9. Branch-Strategie

```
main
 └── feature/ABO-XXXX-httpget-connector
      ├── Commits für Core Implementation
      ├── Commits für Tests
      └── Commits für Doku
      → PR nach main (Code Review required)
```

**Branch-Namenskonvention**: `feature/ABO-XXXX-httpget-connector`  
**PR-Anforderungen**:
- ✅ Linked Issue/Ticket
- ✅ Unit Tests für alle neuen Business-Logic-Pfade
- ✅ Keine `Console.WriteLine` / Debug-Artefakte
- ✅ Passing CI (Build + Tests)
- ✅ Änderungsumfang < 500 Zeilen

---

## 10. Definition of Done (DoD)

- [ ] `IConnector.HttpGetAsync` Methode definiert und vollständig implementiert
- [ ] URL-Validierung (Schema, Parsbarkeit) implementiert
- [ ] SSRF-Schutz (Blockliste für private IPs) implementiert
- [ ] Response-Truncation auf 100 KB implementiert
- [ ] Timeout-Handling mit CancellationToken implementiert
- [ ] `HttpGetTool` implementiert und in `SpecialistAgent` registriert
- [ ] Alle Unit Tests grün (min. 9 Test Cases)
- [ ] `tools.md` und `architecture.md` aktualisiert
- [ ] PR reviewed und approved durch Tech Lead
- [ ] CI-Build erfolgreich durchgelaufen
- [ ] Kein `console.log` / `Console.WriteLine` im produktiven Code

---

## 11. Offene Fragen (Open Questions)

| # | Frage | Verantwortlich | Fälligkeit |
|---|-------|---------------|-----------|
| OQ-1 | Soll es auch eine `http_post`-Variante geben? Scope für dieses Issue oder separates Epic? | PMO Lead | Vor Sprint-Start |
| OQ-2 | Soll der `Authorization`-Header explizit aus dem Projekt-`notes.md` oder `appsettings.json` gespeist werden? | Tech Lead + Architect | Vor Implementierung |
| OQ-3 | Muss eine Allowlist für erlaubte Domains konfigurierbar sein (via `appsettings.json`)? | Tech Lead | Vor Implementierung |
| OQ-4 | Soll das Tool auch im `ManagerAgent` oder `PmoAgent` verfügbar sein, oder nur im `SpecialistAgent`? | PMO Lead | Vor Sprint-Start |

---

*Ende der Technischen Spezifikation v1.0*
