# Services, Integrations, and Web API

This file documents the background services (`BackgroundService`), the user data service, integrations, and all available HTTP endpoints of the ABO application.

---

## Background Services

### `MattermostListenerService`
- **Namespace**: `Abo.Integrations.Mattermost`
- **Type**: `BackgroundService` (IHostedService)
- **Purpose**: Connects via **WebSocket** to the Mattermost server and continuously listens for new posts (`posted` events) in channels and direct messages.
- **Flow**:
  1. Opens a WebSocket connection to `{MattermostBaseUrl}/websocket` with `Bearer` token authentication.
  2. Receives incoming events. Non-`posted` events are ignored.
  3. On `hello` or `status_change` events, the bot user ID is cached for self-identification.
  4. The bot's own messages are filtered out using the cached user ID (loop prevention).
  5. The `AgentSupervisor` selects the appropriate agent (with conversation history as context).
  6. During agent processing, a typing indicator (`typing`) is sent to the channel every 5 seconds.
  7. The final response is sent back via the REST API (`MattermostClient.SendMessageAsync`).
- **Error Handling**: On connection drops, a reconnect is automatically attempted after 10 seconds.
- **Configuration**: Requires `Integrations:Mattermost:BaseUrl` and `Integrations:Mattermost:BotToken`.

---

### `QuizService`
- **Namespace**: `Abo.Services`
- **Type**: `BackgroundService` (IHostedService)
- **Purpose**: Automatically sends quiz questions **hourly** to all subscribed Mattermost channels.
- **Flow**:
  1. Waits 30 seconds after startup to allow other services to initialize.
  2. Loads all users via `UserService` and filters those with `IsSubscribedToQuiz == true`.
  3. Invokes the `QuizAgent` via the orchestrator for each subscribed channel (trigger: `SYSTEM_EVENT: HOURLY_QUESTION_TRIGGER`).
  4. Sends the agent's response via `MattermostClient.SendMessageAsync`.
  5. Waits 1 hour and repeats.

---

## Application Services

### `SessionService`
- **Namespace**: `Abo.Core`
- **Lifecycle**: Singleton
- **Purpose**: Manages the **in-memory conversation history** per session (channel).
- **Details**:
  - Stores a maximum of **20 messages** per session (oldest are automatically removed).
  - Thread-safe via `ConcurrentDictionary` and `lock`.
  - The `Orchestrator` uses the `SessionId` (= Mattermost Channel ID or `web-session`) as the key.
- **Methods**:
  - `GetHistory(sessionId)` – Returns the message list (creates it if needed).
  - `AddMessage(sessionId, message)` – Adds a message and trims the history if necessary.
  - `ClearHistory(sessionId)` – Clears the entire history for a session.

---

### `UserService`
- **Namespace**: `Abo.Services`
- **Lifecycle**: Singleton
- **Purpose**: Persists user data (Mattermost user ID, username, roles, quiz subscription status) in `Data/users.json`.
- **File format**: JSON dictionary with the username as the key.
- **Thread Safety**: All read and write operations are protected by a `lock` object.
- **Key Methods**:
  - `GetOrCreateUser(mattermostId, username)` – Returns an existing user or creates a new one.
  - `UpdateUser(user)` – Saves an updated user instance.
  - `HasRole(mattermostId, role)` – Checks whether a user has a specific role.
  - `AddRole(mattermostId, role)` – Adds a role to a user.
  - `RemoveRole(mattermostId, role)` – Removes a role from a user.
  - `GetAllUsers()` – Returns all users as a list.

---

## Integrations

### Mattermost

#### `MattermostClient`
- **Namespace**: `Abo.Integrations.Mattermost`
- **Type**: Typed `HttpClient` (DI-registered)
- **Purpose**: REST communication with the Mattermost API (`/api/v4/`).
- **Configuration**: `Integrations:Mattermost:BaseUrl` and `Integrations:Mattermost:BotToken`
- **Methods**:
  - `SendMessageAsync(channelId, message, rootId?)` – Sends a message to a channel (optionally as a thread reply with `rootId`).
  - `SendTypingAsync(channelId, parentId?)` – Sends a typing indicator.
  - `GetUsernameAsync(userId)` – Resolves a Mattermost user ID to a username.

#### `MattermostOptions`
- Configuration class for the `Integrations:Mattermost` section in `appsettings.json`.
- Fields: `BaseUrl`, `BotToken`

---

### XpectoLive

#### `XpectoLiveClient`
- **Namespace**: `Abo.Integrations.XpectoLive`
- **Type**: Typed `HttpClient`
- **Purpose**: Communication with the XpectoLive Backoffice REST API.
- **Authentication**: `x-api-key` header.
- **Configuration**: `Integrations:XpectoLive:BaseUrl` and `Integrations:XpectoLive:ApiKey`
- **Method**: `GetTicketsAsync(queryParameters)` – Retrieves tickets (currently mock implementation).

#### `XpectoLiveWikiClient` / `IXpectoLiveWikiClient`
- **Namespace**: `Abo.Integrations.XpectoLive`
- **Purpose**: Full implementation for the XpectoLive Wiki API.
- **Configuration**: Identical to `XpectoLiveClient`.
- **Methods** (selection):
  - `GetSpacesAsync()` – Retrieve all wiki spaces.
  - `CreateSpaceAsync(spaceNew)` – Create a new space.
  - `GetSpaceAsync(spaceId)` – Retrieve a space.
  - `GetSpaceInfoAsync(spaceId)` – Page information for a space.
  - `CreatePageAsync(spaceId, pageNew)` – Create a new page.
  - `GetPageAsync(spaceId, pageId)` – Read a page.
  - `UpdatePageDraftAsync(spaceId, pageId, contentUpdate)` – Update a page draft.
  - `PublishPageDraftAsync(spaceId, pageId)` – Publish a draft.
  - `MovePageAsync(...)` / `CopyPageAsync(...)` – Move/copy a page.
  - `JoinCollaborativeRoomAsync(...)` / `LeaveCollaborativeRoomAsync(...)` – Join/leave a collaborative editing room.
  - `RdpAsync(domain, user, computerName)` – Initiate an RDP session via XpectoLive.

#### `XpectoLiveOptions`
- Configuration class for the `Integrations:XpectoLive` section.
- Fields: `BaseUrl`, `ApiKey`

---

## Web API Endpoints

The ABO application exposes a minimal REST API defined in `Program.cs` using ASP.NET Core Minimal APIs.

### `GET /api/status`
- **Description**: Returns the current status of the ABO application (health check).
- **Authentication**: None
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
- **Description**: Lists all available BPMN process IDs (filenames without `.bpmn` extension from `Data/Processes/`).
- **Response (200)**: JSON array of process ID strings, e.g. `["Type_Dev_Sprint", "Type_Doc_Update"]`

---

### `GET /api/processes/{id}`
- **Description**: Returns the full BPMN 2.0 XML definition of a process.
- **Parameters**: `id` – The process ID (corresponds to the filename without `.bpmn`).
- **Response (200)**: BPMN XML (`Content-Type: application/xml`)
- **Response (400)**: If `id` contains invalid characters (`..`, `/`, `\`).
- **Response (404)**: If the process is not found.
- **Usage**: Used by the Web UI at `/processes/index.html` to display processes in the BPMN viewer.

---

### `GET /api/issues/{id}/status`
- **Description**: Returns the current status of a running issue (contents of `Data/Issues/{id}/status.json`).
- **Parameters**: `id` – The issue ID (e.g. `1001`).
- **Response (200)**: JSON object with issue status fields:
  ```json
  {
    "IssueId": "1001",
    "CurrentStepId": "Task_WriteDoc",
    "Status": "Active",
    "LastUpdated": "2025-01-15T10:30:00Z"
  }
  ```
- **Response (400)**: If `id` contains invalid characters.
- **Response (404)**: If no issue with this ID exists.

---

### `POST /api/interact`
- **Description**: The main chat endpoint. Processes a user message, selects the appropriate agent, and returns the AI response.
- **Request Body** (`application/json`):
  ```json
  {
    "message": "Start the quiz!",
    "userName": "max.muster",
    "userId": "mm-user-id-123",
    "sessionId": "channel-id-abc"
  }
  ```
  - `message` (required): The user message.
  - `userName` (optional): Display name of the user. Default: `"Web User"`.
  - `userId` (optional): Unique user ID. Default: `sessionId`.
  - `sessionId` (optional): Session/channel ID for conversation history. Default: `"web-session"`.
- **Response (200)**:
  ```json
  {
    "output": "Here is your next quiz question: ..."
  }
  ```
- **Response (400)**: If `message` is empty.

---

## Static Files (Web UI)

ABO serves static files from the `wwwroot/` directory. Key pages:

| Path | Description |
|------|-------------|
| `/` or `/index.html` | Main Chat UI |
| `/processes/index.html` | BPMN Process Viewer (displays `.bpmn` files graphically) |
