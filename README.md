# 🤖 Agsoro Bot Orchestrator (ABO)

**ABO** ist ein schlankes, datenschutzorientiertes KI-Agenten-Orchestrierungsframework, entwickelt in **C# / .NET 10**. Es dient als intelligente Schicht über intern entwickelten Ticket- und Arbeitsverfolgungssystemen und verwandelt statische Daten in ein aktives, automatisiertes Projektmanagement-Ökosystem.

Das Framework wurde für Organisationen entwickelt, die **Datensouveränität** priorisieren. Es verwendet ein "Native Orchestrator"-Muster und kommuniziert mit KI-Modellen über Standard-REST/API-Aufrufe – ob lokal gehostet oder über einen sicheren privaten Gateway.

---

## 🚀 Kernfunktionen

* **Datenschutz-orientiertes Design:** ABO ist darauf ausgelegt, vollständig innerhalb des eigenen sicheren Netzwerks zu laufen. Es benötigt nur einen REST-API-Endpunkt – ohne proprietäre SDKs oder "Phone-Home"-Telemetrie.
* **Reines .NET 10:** Implementiert mit Standard-`HttpClient` und `System.Text.Json` für maximale Performance, Transparenz und Wartbarkeit.
* **Backend-Agnostisch:** Nahtloses Wechseln zwischen lokalen Inferenzservern, privaten Cloud-Instanzen oder verwalteten Gateways – nur durch Aktualisierung der Base-URL.
* **Intelligente Agentenauswahl:** Leitet Anfragen automatisch an den am besten geeigneten spezialisierten Agenten weiter (z. B. Quiz, Begrüßung, PMO, Employee).
* **BPMN-basiertes Projektmanagement:** Vollständige Unterstützung für strukturierte, mehrstufige Workflows über BPMN 2.0 Prozessdefinitionen.
* **Mattermost-Integration:** Empfängt Nachrichten via WebSocket und antwortet via REST direkt in Channels und Direktnachrichten.

---

## ⚡ Quickstart

### Voraussetzungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Zugang zu einem REST-kompatiblen KI-Modell-Endpunkt (z. B. OpenRouter, lokale Ollama-Instanz)
- Optional: Mattermost-Instanz für Chat-Integration

### 1. Repository klonen

```bash
git clone https://github.com/agsoro/abo.git
cd abo
```

### 2. Konfiguration einrichten

Lege eine `appsettings.json` im `Abo/`-Verzeichnis an (oder nutze User Secrets für die lokale Entwicklung):

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
    "Mattermost": {
      "BaseUrl": "https://your-mattermost.example.com",
      "BotToken": "SECRET"
    },
    "XpectoLive": {
      "BaseUrl": "https://backoffice.xpectolive.com/api",
      "ApiKey": "SECRET"
    }
  }
}
```

Für sensible Werte (Tokens, API-Keys) bitte **keine** Klartextwerte in `appsettings.json` ablegen – stattdessen .NET User Secrets oder Umgebungsvariablen verwenden. Siehe [Konfiguration & Secrets](Abo/Docs/configuration.md).

### 3. Anwendung starten

```bash
cd Abo
dotnet run
```

Die Anwendung ist anschließend erreichbar unter:
- **Chat-UI:** `http://localhost:5000/`
- **BPMN-Prozess-Viewer:** `http://localhost:5000/processes/index.html`
- **Health-Check:** `http://localhost:5000/api/status`

---

## 🏗️ Architektur: Der „Agent Loop"

ABO arbeitet nach einem **Controller-Worker**-Loop, der sicherstellt, dass die KI niemals direkten, unkontrollierten Zugriff auf interne Daten hat:

1. **Intelligente Auswahl:** Der `AgentSupervisor` analysiert die Anfrage per LLM und wählt den geeignetsten spezialisierten Agenten.
2. **Reasoning:** Der Agent stellt seinen `SystemPrompt` und seine `ToolDefinitions` bereit. Der Orchestrator sendet einen `POST`-Request an den KI-Endpunkt. Das Modell gibt einen "Tool Call" (JSON) zurück.
3. **Lokale Ausführung:** Der C#-Orchestrator parst den JSON-Tool-Call und ruft die entsprechende interne C#-Methode auf.
4. **Synthese:** Das Ergebnis wird an das Modell zurückgesendet, um eine abschließende, menschenlesbare Zusammenfassung zu erzeugen.

Vollständige Architekturbeschreibung: [Abo/Docs/architecture.md](Abo/Docs/architecture.md)

---

## 📂 Projektstruktur

```
/
├── Abo/                        - Hauptprojekt
│   ├── Agents/                 - Agenten-Implementierungen (IAgent)
│   ├── Core/
│   │   ├── Connectors/         - IConnector, LocalWindowsConnector
│   │   ├── AgentSupervisor.cs  - Intelligente Agentenauswahl
│   │   ├── Orchestrator.cs     - Kern-Loop-Logik & REST-Client
│   │   └── SessionService.cs   - In-Memory-Gesprächshistorie
│   ├── Contracts/              - JSON-Schemas und DTOs
│   ├── Data/
│   │   ├── Processes/          - BPMN-Prozessdefinitionen (.bpmn)
│   │   ├── Projects/           - Projektinstanzen (info.md, status.json)
│   │   ├── Environments/       - environments.json
│   │   └── Quiz/               - Quiz-Daten (leaderboard.json)
│   ├── Docs/                   - Projektdokumentation (diese Dateien)
│   ├── Integrations/
│   │   ├── Mattermost/         - Mattermost HTTP-Client & WebSocket-Listener
│   │   └── XpectoLive/         - XpectoLive HTTP-Client & Wiki-Client
│   ├── Models/                 - Datenbankmodelle / Entitäten (User)
│   ├── Services/               - Geschäftslogik (UserService, QuizService)
│   ├── Tools/                  - IAboTool-Implementierungen
│   │   └── Connector/          - Konnektor-Tools (ReadFileTool, GitTool, ...)
│   └── wwwroot/                - Statische Web-UI-Dateien
└── Abo.Tests/                  - Unit-Tests
```

---

## 📚 Dokumentation

| Datei | Inhalt |
|---|---|
| [architecture.md](Abo/Docs/architecture.md) | Gesamtarchitektur, Agent-Loop, Connector, Verzeichnisstruktur |
| [agents.md](Abo/Docs/agents.md) | Alle Agenten mit Beschreibung und Fähigkeiten |
| [tools.md](Abo/Docs/tools.md) | Alle Tools mit Parametern und Beispielen |
| [services.md](Abo/Docs/services.md) | Services, Integrationen, Web-API-Endpunkte |
| [configuration.md](Abo/Docs/configuration.md) | Konfiguration, Secret Management, alle Konfig-Keys |

---

## 📄 Lizenz

Siehe [LICENSE](LICENSE).
