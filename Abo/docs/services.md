# Services, Integrations und Web-API

Diese Datei dokumentiert die Hintergrunddienste (`BackgroundService`), den Benutzerdaten-Service, die Integrationen und alle verfügbaren HTTP-Endpunkte der ABO-Anwendung.

---

## Hintergrunddienste (Background Services)

### `MattermostListenerService`
- **Namespace**: `Abo.Integrations.Mattermost`
- **Typ**: `BackgroundService` (IHostedService)
- **Aufgabe**: Verbindet sich per **WebSocket** mit dem Mattermost-Server und lauscht kontinuierlich auf neue Beiträge (`posted`-Events) in Channels und Direktnachrichten.
- **Ablauf**:
  1. Öffnet eine WebSocket-Verbindung zu `{MattermostBaseUrl}/websocket` mit `Bearer`-Token-Authentifizierung.
  2. Empfängt eingehende Events. Nicht-`posted`-Events werden ignoriert.
  3. Beim `hello`- oder `status_change`-Event wird die Bot-User-ID zur Selbsterkennung gecacht.
  4. Eigene Nachrichten des Bots werden anhand der gecachten User-ID herausgefiltert (Loop-Prävention).
  5. Der `AgentSupervisor` wählt den passenden Agenten aus (mit Gesprächshistorie als Kontext).
  6. Während der Agenten-Verarbeitung wird alle 5 Sekunden ein Tipp-Indikator (`typing`) an den Channel gesendet.
  7. Die fertige Antwort wird via REST-API (`MattermostClient.SendMessageAsync`) zurückgeschickt.
- **Fehlerbehandlung**: Bei Verbindungsabbrüchen wird nach 10 Sekunden automatisch ein Reconnect versucht.
- **Konfiguration**: Benötigt `Integrations:Mattermost:BaseUrl` und `Integrations:Mattermost:BotToken`.

---

### `QuizService`
- **Namespace**: `Abo.Services`
- **Typ**: `BackgroundService` (IHostedService)
- **Aufgabe**: Sendet **stündlich** automatisch Quiz-Fragen an alle abonnierten Mattermost-Channels.
- **Ablauf**:
  1. Wartet 30 Sekunden nach dem Start, um andere Dienste hochfahren zu lassen.
  2. Lädt alle Benutzer via `UserService` und filtert nach `IsSubscribedToQuiz == true`.
  3. Ruft für jeden abonnierten Channel den `QuizAgent` via Orchestrator auf (Trigger: `SYSTEM_EVENT: HOURLY_QUESTION_TRIGGER`).
  4. Sendet die Antwort des Agenten via `MattermostClient.SendMessageAsync`.
  5. Wartet 1 Stunde und wiederholt den Vorgang.

---

## Anwendungs-Services

### `SessionService`
- **Namespace**: `Abo.Core`
- **Lebenszyklus**: Singleton
- **Aufgabe**: Verwaltet die **In-Memory-Gesprächshistorie** pro Session (Channel).
- **Besonderheiten**:
  - Speichert maximal **20 Nachrichten** pro Session (älteste werden automatisch entfernt).
  - Thread-sicher durch `ConcurrentDictionary` und `lock`.
  - Der `Orchestrator` verwendet den `SessionId` (= Mattermost Channel-ID oder `web-session`) als Schlüssel.
- **Methoden**:
  - `GetHistory(sessionId)` – Gibt die Nachrichtenliste zurück (erstellt sie bei Bedarf).
  - `AddMessage(sessionId, message)` – Fügt eine Nachricht hinzu und kürzt ggf. die History.
  - `ClearHistory(sessionId)` – Löscht die gesamte History einer Session.

---

### `UserService`
- **Namespace**: `Abo.Services`
- **Lebenszyklus**: Singleton
- **Aufgabe**: Persistiert Benutzerdaten (Mattermost-User-ID, Username, Rollen, Quiz-Abonnement-Status) in `Data/users.json`.
- **Dateiformat**: JSON-Dictionary mit dem Benutzernamen als Schlüssel.
- **Thread-Sicherheit**: Alle Lese- und Schreiboperationen sind durch ein `lock`-Objekt gesichert.
- **Wichtige Methoden**:
  - `GetOrCreateUser(mattermostId, username)` – Gibt einen bestehenden Benutzer zurück oder legt einen neuen an.
  - `UpdateUser(user)` – Speichert eine aktualisierte Benutzerinstanz.
  - `HasRole(mattermostId, role)` – Prüft, ob ein Benutzer eine bestimmte Rolle besitzt.
  - `AddRole(mattermostId, role)` – Fügt einem Benutzer eine Rolle hinzu.
  - `RemoveRole(mattermostId, role)` – Entfernt eine Rolle von einem Benutzer.
  - `GetAllUsers()` – Gibt alle Benutzer als Liste zurück.

---

## Integrationen

### Mattermost

#### `MattermostClient`
- **Namespace**: `Abo.Integrations.Mattermost`
- **Typ**: Typisierter `HttpClient` (DI-registriert)
- **Aufgabe**: REST-Kommunikation mit der Mattermost API (`/api/v4/`).
- **Konfiguration**: `Integrations:Mattermost:BaseUrl` und `Integrations:Mattermost:BotToken`
- **Methoden**:
  - `SendMessageAsync(channelId, message, rootId?)` – Sendet eine Nachricht in einen Channel (optional als Thread-Reply mit `rootId`).
  - `SendTypingAsync(channelId, parentId?)` – Sendet einen Tipp-Indikator.
  - `GetUsernameAsync(userId)` – Löst eine Mattermost-User-ID in einen Benutzernamen auf.

#### `MattermostOptions`
- Konfigurationsklasse für `Integrations:Mattermost`-Sektion in `appsettings.json`.
- Felder: `BaseUrl`, `BotToken`

---

### XpectoLive

#### `XpectoLiveClient`
- **Namespace**: `Abo.Integrations.XpectoLive`
- **Typ**: Typisierter `HttpClient`
- **Aufgabe**: Kommunikation mit der XpectoLive Backoffice REST-API.
- **Authentifizierung**: `x-api-key`-Header.
- **Konfiguration**: `Integrations:XpectoLive:BaseUrl` und `Integrations:XpectoLive:ApiKey`
- **Methode**: `GetTicketsAsync(queryParameters)` – Ruft Tickets ab (aktuell Mock-Implementierung).

#### `XpectoLiveWikiClient` / `IXpectoLiveWikiClient`
- **Namespace**: `Abo.Integrations.XpectoLive`
- **Aufgabe**: Vollständige Implementierung für die XpectoLive Wiki-API.
- **Konfiguration**: Identisch mit `XpectoLiveClient`.
- **Methoden** (Auswahl):
  - `GetSpacesAsync()` – Alle Wiki-Spaces abrufen.
  - `CreateSpaceAsync(spaceNew)` – Neuen Space erstellen.
  - `GetSpaceAsync(spaceId)` – Einen Space abrufen.
  - `GetSpaceInfoAsync(spaceId)` – Seiteninformationen eines Spaces.
  - `CreatePageAsync(spaceId, pageNew)` – Neue Seite anlegen.
  - `GetPageAsync(spaceId, pageId)` – Eine Seite lesen.
  - `UpdatePageDraftAsync(spaceId, pageId, contentUpdate)` – Entwurf einer Seite aktualisieren.
  - `PublishPageDraftAsync(spaceId, pageId)` – Entwurf veröffentlichen.
  - `MovePageAsync(...)` / `CopyPageAsync(...)` – Seite verschieben/kopieren.
  - `JoinCollaborativeRoomAsync(...)` / `LeaveCollaborativeRoomAsync(...)` – Kollaborativen Editing-Raum betreten/verlassen.
  - `RdpAsync(domain, user, computerName)` – RDP-Sitzung über XpectoLive initiieren.

#### `XpectoLiveOptions`
- Konfigurationsklasse für `Integrations:XpectoLive`-Sektion.
- Felder: `BaseUrl`, `ApiKey`

---

## Web-API Endpunkte

Die ABO-Anwendung stellt eine minimale REST-API bereit, die über `Program.cs` mit ASP.NET Core Minimal APIs definiert ist.

### `GET /api/status`
- **Beschreibung**: Gibt den aktuellen Status der ABO-Anwendung zurück (Health-Check).
- **Authentifizierung**: Keine
- **Response (200)**:
  ```json
  {
    "status": "Running",
    "model": "anthropic/claude-3-haiku",
    "hasApiKey": true
  }
  ```

---

### `GET /api/processes`
- **Beschreibung**: Listet alle verfügbaren BPMN-Prozess-IDs auf (Dateinamen ohne `.bpmn`-Erweiterung aus `Data/Processes/`).
- **Response (200)**: JSON-Array mit Prozess-ID-Strings, z. B. `["Type_Dev_Sprint", "Type_Doc_Update"]`

---

### `GET /api/processes/{id}`
- **Beschreibung**: Gibt die vollständige BPMN 2.0 XML-Definition eines Prozesses zurück.
- **Parameter**: `id` – Die Prozess-ID (entspricht dem Dateinamen ohne `.bpmn`).
- **Response (200)**: BPMN-XML (`Content-Type: application/xml`)
- **Response (400)**: Wenn `id` ungültige Zeichen enthält (`..`, `/`, `\`).
- **Response (404)**: Wenn der Prozess nicht gefunden wird.
- **Verwendung**: Wird vom Web-UI unter `/processes/index.html` genutzt, um Prozesse im BPMN-Viewer darzustellen.

---

### `GET /api/projects/{id}/status`
- **Beschreibung**: Gibt den aktuellen Status eines laufenden Projekts zurück (Inhalt von `Data/Projects/{id}/status.json`).
- **Parameter**: `id` – Die Projekt-ID (z. B. `1001`).
- **Response (200)**: JSON-Objekt mit Projektstatusfeldern:
  ```json
  {
    "ProjectId": "1001",
    "CurrentStepId": "Task_WriteDoc",
    "Status": "Active",
    "LastUpdated": "2025-01-15T10:30:00Z"
  }
  ```
- **Response (400)**: Wenn `id` ungültige Zeichen enthält.
- **Response (404)**: Wenn kein Projekt mit dieser ID existiert.
- **Hinweis**: Dieser Link wird von `start_project` automatisch als `StatusLink` im `active_projects.json`-Eintrag gesetzt.

---

### `POST /api/interact`
- **Beschreibung**: Der Haupt-Chat-Endpunkt. Verarbeitet eine Benutzernachricht, wählt den geeigneten Agenten aus und gibt die KI-Antwort zurück.
- **Request-Body** (`application/json`):
  ```json
  {
    "message": "Starte das Quiz!",
    "userName": "max.muster",
    "userId": "mm-user-id-123",
    "sessionId": "channel-id-abc"
  }
  ```
  - `message` (required): Die Benutzernachricht.
  - `userName` (optional): Anzeigename des Benutzers. Standard: `"Web User"`.
  - `userId` (optional): Eindeutige Benutzer-ID. Standard: `sessionId`.
  - `sessionId` (optional): Session-/Channel-ID für die Gesprächshistorie. Standard: `"web-session"`.
- **Response (200)**:
  ```json
  {
    "output": "Hier ist deine nächste Quiz-Frage: ..."
  }
  ```
- **Response (400)**: Wenn `message` leer ist.

---

## Statische Dateien (Web-UI)

ABO dient statische Dateien aus dem `wwwroot/`-Verzeichnis. Wichtige Seiten:

| Pfad | Beschreibung |
|------|-------------|
| `/` oder `/index.html` | Haupt-Chat-UI |
| `/processes/index.html` | BPMN-Prozess-Viewer (visualisiert `.bpmn`-Dateien grafisch) |
