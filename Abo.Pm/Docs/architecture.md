# ABO Architecture

The Agsoro Bot Orchestrator (ABO) is built on a **Controller-Worker** loop that ensures maximum data privacy and data sovereignty. It implements a "Native Orchestrator" pattern.

---

## The Agent Loop

The core orchestration loop ensures that the AI never has direct, uncontrolled access to internal data. All actions are mediated by the C# orchestrator:

1. **Intelligent Selection**: When a request comes in, the `AgentSupervisor` uses the LLM to analyze the intent and select the most appropriate specialized agent (e.g. `QuizAgent`, `HelloWorldAgent`, `PmoAgent`, or `SpecialistAgent`).
2. **Reasoning**: The selected agent provides its `SystemPrompt` and `ToolDefinitions`. The orchestrator sends a REST `POST` request to the AI endpoint. The model returns a "Tool Call" in JSON.
3. **Local Execution**: The C# orchestrator parses the JSON tool call and invokes the corresponding internal C# method.
4. **Synthesis**: The result is sent back to the AI model to generate a final, human-readable summary or action confirmation.

---

## Lightweight Architecture (vs. MCP)

ABO deliberately avoids the full MCP server/client overhead (Model Context Protocol) in favor of a simpler, lightweight architecture:

- Instead of managing external MCP servers, tools are defined directly in C#.
- Instead of complex transport protocols, ABO serializes its internal capabilities in the widely used standard JSON `tools` array format (compatible with OpenRouter, OpenAI, and most major models).
- This keeps the orchestrator extremely lightweight – only basic JSON serialization is required to expose private C# methods to the AI.

---

## Data Privacy

Since all tool executions run locally in C#, sensitive systems are never directly exposed to the AI service. The AI only sees the specific data provided during the reasoning and synthesis phases.

---

## Issue and Process Management (PMO Layer)

ABO implements a full BPMN-based issue management system:

- **Process definitions** are stored as `.bpmn` files in `/Data/Processes/`.
- **Issue instances** are managed in `/Data/Issues/{issueId}/`, consisting of:
  - `info.md` – Issue goals, context, and initial parameters.
  - `status.json` – Current BPMN step, status, and timestamps.
- **`active_issues.json`** is the central list of all running issues (in `/Data/Issues/`).
- The `PmoAgent` designs processes and roles; the `ManagerAgent` delegates work to `SpecialistAgent` instances that perform the actual work.
- The current issue status is accessible via the REST endpoint `GET /api/issues/{id}/status`.

---

## Secure Connector (SpecialistAgent)

The `SpecialistAgent` uses a **connector pattern** for secure filesystem and network access:

1. The agent calls `checkout_task` with a `issueId`.
2. ABO resolves the configured **environment** (`ConnectorEnvironment`) of the issue (stored in `environments.json`).
3. A `LocalWindowsConnector` is instantiated and bound to the environment's directory.
4. All subsequent filesystem and shell tools (`read_file`, `write_file`, `git`, `dotnet`, `python`, `search_regex`, `http_get`, etc.) are **confined** to that directory (filesystem) or security-validated (HTTP).
5. After the task is completed (`complete_task`), the connector is released.

```
[SpecialistAgent]
  ↓ checkout_task(issueId)
[ABO-Core: Environment Resolution]
  ↓ ConnectorEnvironment { Dir = "C:\src\issue" }
[LocalWindowsConnector]
  ↓ read_file / write_file / git / dotnet / python / search_regex / http_get ...
[Filesystem (confined to Dir) / External HTTP (SSRF-protected)]
```

### Available Connector Tools

| Tool | Class | Description |
|---|---|---|
| `read_file` | `ReadFileTool` | Read a file by relative path |
| `write_file` | `WriteFileTool` | Write/create a file |
| `delete_file` | `DeleteFileTool` | Delete a file |
| `list_dir` | `ListDirTool` | List directory contents |
| `mkdir` | `MkDirTool` | Create a new directory |
| `git` | `GitTool` | Execute git commands |
| `dotnet` | `DotnetTool` | Execute .NET CLI commands |
| `python` | `PythonTool` | Execute Python commands (requires Python in PATH) |
| `search_regex` | `SearchRegexTool` | Search for regex patterns across files and filenames |
| `http_get` | `HttpGetTool` | Send HTTP GET requests to external APIs (SSRF-protected, 100 KB cap) |

### HttpGet Security Architecture

The `http_get` tool delegates to `LocalWindowsConnector.HttpGetAsync`, which enforces a multi-layered security model via the separate `HttpGetSecurityHelper` class:

```
[HttpGetTool.ExecuteAsync]
  ↓ URL-Parsing + Parameter-Validation
[LocalWindowsConnector.HttpGetAsync]
  ↓ Schema-Check (http/https only)
  ↓ SSRF-Check: HttpGetSecurityHelper.CheckSsrfAsync(uri)
    ├── Loopback-Hostname-Block (localhost, 127.0.0.1, [::1])
    ├── Direct IP: IPAddress.IsLoopback() + IsPrivateIpAddress()
    └── DNS-Resolution: alle Adressen prüfen (RFC-1918 Bitmasken)
  ↓ HttpClient.SendAsync (ResponseHeadersRead)
  ↓ Body-Truncation (max 100 KB)
[External HTTP Target / Error Response]
```

---

## External Integration Pattern

When ABO interacts with external services (e.g. XpectoLive or Mattermost), **HTTP infrastructure** is strictly separated from **AI tool execution**:

- **`/Integrations/{Platform}`**: Contains typed HTTP clients (`XpectoLiveClient`, `MattermostClient`) that handle authentication, retries, serialization, and API routing. These classes have no knowledge of AI or tool-calling schemas.
- **`/Tools/{Domain}`**: Contains lightweight C# plugins implementing `IAboTool`. These tools receive the LLM's arguments as JSON, request the needed client via Dependency Injection, and convert the infrastructure responses into plain text for the LLM.

This pattern prevents LLM tools from being bloated with authentication and HTTP retry logic.

---

## Web API & Entry Points

ABO exposes a minimal REST API (ASP.NET Core Minimal APIs) and a static Web UI:

| Endpoint | Method | Description |
|---|---|---|
| `/api/status` | GET | Health check: returns model and configuration status |
| `/api/processes` | GET | Lists all available BPMN process IDs |
| `/api/processes/{id}` | GET | Returns the BPMN XML definition of a process |
| `/api/issues/{id}/status` | GET | Returns the current BPMN step and status of a issue |
| `/api/interact` | POST | Main chat endpoint: receives messages, selects agent, returns response |
| `/api/issues` | GET | Lists all active issues (name, ID, status) |
| `/api/llm-consumption` | GET | Returns LLM consumption statistics (supports `?limit=N`) |
| `/api/open-work` | GET | Returns currently open work items across all active issues |
| `/api/sessions` | GET | Returns active agent sessions with history length and timestamps |
| `/` | GET | Web UI (`wwwroot/index.html`) – Chat interface |
| `/processes/index.html` | GET | BPMN process viewer |
| `/agents/index.html` | GET | Agent / session overview |
| `/open-work/index.html` | GET | Open work dashboard |
| `/llm-traffic/index.html` | GET | LLM traffic log viewer |
| `/llm-stats/index.html` | GET | LLM consumption statistics dashboard |

Detailed API documentation with request/response schemas: see [services.md](services.md).

---

## Runtime Data Structure (`/Data/`)

The `/Data/` directory contains all ABO runtime data. These files are written and read at runtime and should **not** be checked into version control (exception: initial data such as BPMN processes).

```
/Data
  /Processes/             - BPMN process definitions (.bpmn files)
  /Issues/
    active_issues.json  - Central list of all active issues
    /{issueId}/
      info.md             - Issue goals, context, initial parameters
      status.json         - Current BPMN step, status, timestamps
      notes.md            - Handover notes between agents/steps
  /Environments/
    environments.json     - Configured connector environments (name → directory path)
  /Quiz/
    leaderboard.json      - Quiz leaderboard with scores
  users.json              - All users (Mattermost ID, username, roles, quiz subscription)
  llm_traffic.jsonl       - LLM requests/responses (debugging, JSONL format)
  llm_consumption.jsonl   - LLM token/cost tracking per agent run (JSONL format)
```

### Key Data Files in Detail

| File | Description |
|---|---|
| `active_issues.json` | JSON array of all running issue IDs and their process type. Managed by `PmoAgent`. |
| `{issueId}/status.json` | Contains `IssueId`, `CurrentStepId`, `Status`, and `LastUpdated`. Updated on every `complete_task`. |
| `{issueId}/info.md` | Created when a issue is started; describes goal, context, and parameters. Immutable after creation. |
| `{issueId}/notes.md` | Handover notes: each agent writes result context here for the next step. |
| `environments.json` | Maps environment names (e.g. `abo`) to local directory paths. Used by the connector for path resolution. |
| `users.json` | Persisted user data: Mattermost ID, username, roles array, quiz subscription flag. |
| `leaderboard.json` | Quiz system scores, indexed by username. |
| `llm_traffic.jsonl` | Debug log: every AI API request and response is logged as a JSON line. Useful for error analysis. Can grow large under heavy usage. |
| `llm_consumption.jsonl` | Token and cost tracking per agent run (prompt tokens, completion tokens, total tokens, cost in USD, session ID). |

---

## Directory Structure

```
/Abo
  /Agents         - Agent implementations (IAgent)
  /Core
    /Connectors   - IConnector, LocalWindowsConnector, HttpGetSecurityHelper,
                    ConnectorEnvironment
    AgentSupervisor.cs
    Orchestrator.cs
    SessionService.cs
  /Contracts      - JSON schemas and DTOs for API interaction
  /Data
    /Processes    - BPMN process definitions (.bpmn)
    /Issues     - Issue instances (info.md, status.json, active_issues.json)
    /Environments - environments.json
    /Quiz         - Quiz data (leaderboard.json)
  /Docs           - This documentation
  /Integrations
    /Mattermost   - Mattermost HTTP client + WebSocket listener
    /XpectoLive   - XpectoLive HTTP client + Wiki client
  /Models         - Database models / entities (User)
  /Services       - Business logic services (UserService, QuizService)
  /Tools          - IAboTool implementations
    /Connector    - Connector tools (ReadFileTool, WriteFileTool, GitTool, DotnetTool,
                    PythonTool, SearchRegexTool, HttpGetTool)
  /wwwroot        - Static Web UI files (Chat, BPMN viewer, Agents, Open Work,
                    LLM Traffic, LLM Stats dashboards)
```
