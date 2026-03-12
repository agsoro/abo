# Analyse — ABO-0004: LLM Consumption Tracking – Projektstatus-Statistiken

**Datum**: 2026-03-12
**Rolle**: Role_Employee (Analyse / Step_Analyze)
**Branch**: feature/abo-0004-llm-consumption-tracking

---

## Ziel

Tracking und Anzeige von LLM-Verbrauchsdaten (API Calls, Token-Verbrauch) pro Session (bzw. Projekt) – mit Anzeige im Web-UI.

---

## Ist-Zustand-Analyse

### Wo LLM-Calls stattfinden
- **Datei**: `Abo/Core/Orchestrator.cs`, Methode `RunAgentLoopAsync()`
- In jedem Loop-Durchlauf wird ein POST an den API-Endpoint gemacht. Die Response enthält Usage-Daten.

### Fehlende Felder in `OpenAIContracts.cs`
- `ChatCompletionResponse` besitzt **kein `Usage`-Feld** – obwohl die OpenAI-kompatible API `usage.prompt_tokens`, `usage.completion_tokens`, `usage.total_tokens` und `usage.cost` liefert.
- Dies muss ergänzt werden.

### Tracking-Infrastruktur
- `Data/llm_traffic.jsonl` enthält alle LLM-Requests/Responses als Rohdaten.
- `Data/Projects/active_projects.json` enthält die Projekt-Liste, aber keine Verbrauchsdaten.
- `Data/Projects/{projectId}/status.json` enthält den Projektstatus, aber keine Verbrauchsdaten.

### Session-Projekt-Verknüpfung
- Die `sessionId` (Mattermost Channel ID oder "web-session") hat **keinen direkten Bezug** zu einer `projectId`.
- Projekte sind unabhängig von Sessions. Es gibt keinen Mechanismus, der `sessionId → projectId` zuordnet.
- **Entscheidung**: Tracking erfolgt daher **session-basiert** (nicht projekt-basiert), aber mit aggregierter Gesamtstatistik.

---

## Implementierungsstrategie

### Schritt 1: `OpenAIContracts.cs` – Usage-Klasse ergänzen
```csharp
public class UsageInfo
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    [JsonPropertyName("cost")]
    public double? Cost { get; set; }
}
```
In `ChatCompletionResponse`:
```csharp
[JsonPropertyName("usage")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public UsageInfo? Usage { get; set; }
```

### Schritt 2: `Orchestrator.cs` – Usage akkumulieren und loggen
- Nach jedem erfolgreichen HTTP-Response die Usage-Daten extrahieren.
- Akkumulierte Daten (totalCalls, totalInputTokens, totalOutputTokens, totalCost) pro `RunAgentLoopAsync`-Aufruf.
- Ergebnis in `Data/llm_consumption.jsonl` schreiben (analoges Format zu `llm_traffic.jsonl`).

**Format des Eintrags** (ein Eintrag pro Agent-Loop-Aufruf / "Conversation Turn"):
```json
{
  "Timestamp": "2026-03-12T19:00:00Z",
  "SessionId": "wok5eu4xkirn5eoygb7u6icxta",
  "CallCount": 3,
  "InputTokens": 5000,
  "OutputTokens": 1500,
  "TotalTokens": 6500,
  "TotalCost": 0.025,
  "Model": "anthropic/claude-haiku-4.5"
}
```

### Schritt 3: `Program.cs` – API Endpoint
Neuer Endpoint: `GET /api/llm-consumption?limit=100`
- Liest `Data/llm_consumption.jsonl` analog zu `/api/llm-traffic`.
- Gibt die Einträge (neueste zuerst) zurück.

### Schritt 4: Frontend – Stats-Seite oder Widget
Option A: Neue Seite `/llm-stats/index.html` (ähnlich wie `/llm-traffic/`).
Option B: Erweiterung der bestehenden `/llm-traffic/index.html` um Aggregations-Widget.

**Entscheidung**: Option A (neue Seite `/llm-stats/`) für Übersichtlichkeit.
- Zeigt aggregierte Statistiken: Gesamtcalls, Gesamttokens, Gesamtkosten.
- Filtert nach SessionId.
- Auto-Update alle 5 Sekunden.
- Navigation in `/index.html` erweitern.

### Schritt 5: Backward-Kompatibilität
- Wenn `usage` null ist (alte Einträge oder Fehler), werden null-Werte verwendet – kein Crash.
- `llm_consumption.jsonl` startet leer und wächst mit der Zeit.
- Bestehende Projekte sind nicht betroffen.

---

## Akzeptanzkriterien-Mapping

| Kriterium | Umsetzung |
|---|---|
| LLM-Call-Zähler wird inkrementiert | `CallCount` per Turn in `Orchestrator.cs` |
| Token-Verbrauch gespeichert | `InputTokens + OutputTokens` aus `Usage` |
| Statistiken im Projektstatus sichtbar | `/llm-stats/index.html` + API Endpoint |
| Bestehende Projekte kompatibel | Keine Änderung an `status.json` oder `active_projects.json` |
| Unit Tests | `Abo.Tests` – Prüfung der UsageInfo-Deserialisierung |

---

## Risiken / Offene Punkte
1. **Usage-Daten nicht immer vorhanden**: Manche LLM-Endpoints liefern kein `usage`. → Null-Checks nötig.
2. **Performance**: `File.AppendAllTextAsync` für `llm_consumption.jsonl` ist unkritisch (1 Eintrag pro Turn, deutlich weniger als `llm_traffic.jsonl`).
3. **Deployment**: EXE läuft im Hintergrund → Build kann nicht deployed werden bis Neustart.

---

## Nächster Schritt
Step_Implement – Role_Dev_Agent soll folgende Dateien anpassen:
1. `Abo/Contracts/OpenAIContracts.cs` – `UsageInfo` + `Usage` hinzufügen
2. `Abo/Core/Orchestrator.cs` – Usage-Tracking und Logging implementieren
3. `Abo/Program.cs` – `GET /api/llm-consumption` Endpoint
4. `Abo/wwwroot/llm-stats/index.html` – Neue Stats-Seite
5. `Abo/wwwroot/index.html` – Navigation erweitern
6. `Abo.Tests/` – Unit Test für UsageInfo-Deserialisierung (optional)