# Ergebnis: Dokumentationsbedarf analysieren (Step_AnalyzeDocNeeds)

**Rolle**: Role_Tech_Lead  
**Datum**: 2025  
**Projekt**: Dokumentations-Update: Abo-Projekt (#1001)

---

## Zusammenfassung

Der Quellcode und die bestehende Dokumentation des ABO-Projekts wurden vollständig analysiert. Die vorhandene Dokumentation ist bereits gut strukturiert, weist jedoch einige Lücken und Verbesserungspotenziale auf. Nachfolgend sind alle Befunde detailliert aufgeführt.

---

## Bestand: Vorhandene Dokumentation

Im Ordner `/Abo/Docs/` existieren folgende Dateien:

| Datei | Inhalt | Qualität |
|---|---|---|
| `agents.md` | Beschreibung aller Agenten (HelloWorld, Quiz, PMO, Employee) | ✅ Vollständig, sehr gut |
| `architecture.md` | Gesamtarchitektur, Agent-Loop, Connector, API-Endpunkte | ✅ Vollständig, sehr gut |
| `tools.md` | Alle Tools mit Parametern und Beschreibungen | ✅ Vollständig, sehr gut |
| `services.md` | Services, Integrationen, Web-API-Endpunkte | ✅ Vollständig, sehr gut |
| `configuration.md` | Konfiguration, Secret Management | ⚠️ Auf Englisch (Inkonsistenz), inhaltlich gut |
| `wiki_schemas.json` | Wiki-API-Schemas | Datendatei, keine Dokumentation nötig |
| `xpectolive-swagger.json` | Swagger-Spezifikation | Datendatei, keine Dokumentation nötig |

---

## Identifizierte Dokumentationslücken

### 🔴 FEHLEND – Kritisch

1. **`README.md` im Projektstamm ist veraltet**  
   - Verweist auf alte Verzeichnisstruktur (`/agents`, `/tools`, `/core`, `/contracts`)  
   - Tatsächliche Struktur weicht ab (z.B. `/Integrations`, `/Services`, `/Models`, `/Data`)  
   - Kein Hinweis auf die BPMN/PMO-Funktionalität, nur allgemeine Bot-Beschreibung  
   - **Maßnahme**: README.md aktualisieren mit korrekter Verzeichnisstruktur und Beschreibung aller Features

2. **`/Abo/Docs/` fehlt im README.md**  
   - Die Docs-Verzeichnisstruktur und die Verlinkung zu den einzelnen Docs fehlen im Stamm-README  
   - **Maßnahme**: Links zu allen Docs-Dateien in README.md aufnehmen

3. **Kein Quickstart / Getting-Started-Guide**  
   - Es gibt keine Schritt-für-Schritt-Anleitung, wie man ABO lokal aufsetzt und startet  
   - `configuration.md` erklärt Konfiguration, aber nicht den vollständigen Ablauf (Clone → Config → Run)  
   - **Maßnahme**: Quickstart-Sektion in README.md oder eigene `getting-started.md` erstellen

4. **`/Abo/Data/`-Struktur undokumentiert**  
   - Die Laufzeitdaten (Prozesse, Projekte, Environments, Quiz, Users) sind nirgends beschrieben  
   - Kein Hinweis auf `active_projects.json`, `status.json`, `info.md`, `environments.json`, `users.json`, `leaderboard.json`  
   - **Maßnahme**: Neue Datei `data-structure.md` erstellen ODER Sektion in `architecture.md` ergänzen

### 🟡 VERBESSERUNGSBEDARF – Mittel

5. **`configuration.md` ist auf Englisch**  
   - Alle anderen Docs-Dateien sind auf Deutsch, `configuration.md` ist auf Englisch  
   - **Maßnahme**: `configuration.md` ins Deutsche übersetzen (Konsistenz)

6. **`CapableModelName`-Konfiguration fehlt in `configuration.md`**  
   - Im Orchestrator (`Orchestrator.cs`) wird `Config:CapableModelName` für Agenten mit `RequiresCapableModel = true` (PmoAgent, EmployeeAgent) verwendet  
   - Dieser Konfigurationsschlüssel ist in `configuration.md` nicht dokumentiert  
   - **Maßnahme**: `CapableModelName` in `configuration.md` ergänzen

7. **`Config:DefaultLanguage`-Konfiguration fehlt**  
   - Der Orchestrator nutzt `Config:DefaultLanguage`, dieser Schlüssel ist nirgends dokumentiert  
   - **Maßnahme**: `DefaultLanguage` in `configuration.md` ergänzen

8. **LLM-Traffic-Log (`llm_traffic.jsonl`) undokumentiert**  
   - Der Orchestrator schreibt alle API-Anfragen/Antworten in `Data/llm_traffic.jsonl`  
   - Dieses Debugging-Feature ist nirgendwo erwähnt  
   - **Maßnahme**: In `architecture.md` oder `configuration.md` als Debugging-Hinweis ergänzen

9. **`InteractRequest`-Schema in `services.md` leicht veraltet**  
   - Das JSON-Request-Beispiel in `services.md` zeigt `userId` als optional, stimmt aber mit `Program.cs` überein – OK  
   - Kein Hinweis darauf, dass die Bot-User-ID durch den `MattermostListenerService` automatisch herausgefiltert wird  
   - **Maßnahme**: Kleiner Hinweis in `services.md`

### 🟢 GUT – Keine Änderung nötig

- `agents.md`: Vollständig, aktuell, korrekt  
- `architecture.md`: Sehr detailliert, deckt alle Kernkomponenten ab  
- `tools.md`: Umfassend, alle Tools mit Parametern dokumentiert  
- `services.md`: Detailliert mit allen API-Endpunkten und Response-Schemas  

---

## Empfohlene Maßnahmen (Priorisiert)

| Prio | Maßnahme | Datei | Aufwand |
|---|---|---|---|
| 1 | README.md aktualisieren (Struktur, Features, Links zu Docs) | `README.md` | Mittel |
| 2 | Quickstart-Guide hinzufügen | `README.md` oder neu: `getting-started.md` | Mittel |
| 3 | Data-Struktur dokumentieren | `architecture.md` erweitern oder `data-structure.md` neu | Klein |
| 4 | `configuration.md` ins Deutsche übersetzen + `CapableModelName` + `DefaultLanguage` ergänzen | `configuration.md` | Klein |
| 5 | LLM-Traffic-Log als Debugging-Feature erwähnen | `architecture.md` oder `configuration.md` | Sehr klein |

---

## Technischer Kontext für den nächsten Schritt

- **Projektstamm**: `C:\src\agsoro\abo`
- **Docs-Verzeichnis**: `Abo/Docs/`
- **Schlüsseldateien**: `README.md`, `Abo/Docs/configuration.md`, `Abo/Docs/architecture.md`
- **Quellcode-Referenzen**:
  - `Orchestrator.cs`: nutzt `Config:CapableModelName` und `Config:DefaultLanguage`
  - `Program.cs`: vollständige DI-Registrierung aller Tools/Agents/Services
  - `AgentSupervisor.cs`: Auswahllogik mit Fallback auf HelloWorldAgent
- **Sprache der Docs**: Deutsch (außer `configuration.md` – muss angeglichen werden)
