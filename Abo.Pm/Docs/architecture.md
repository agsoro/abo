# ABO Architecture

The Agsoro Bot Orchestrator (ABO) is built on a **Controller-Worker** loop that ensures maximum data privacy and data sovereignty. It implements a "Native Orchestrator" pattern.

---

## The Agent Loop

The core orchestration loop ensures that the AI never has direct, uncontrolled access to internal data. All actions are mediated by the C# orchestrator:

1. **Intelligent Selection**: When a request comes in, the `AgentSupervisor` uses the LLM to analyze the intent and select the most appropriate specialized agent (e.g. `ManagerAgent` or `SpecialistAgent`).
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

## Issue and Workflow Management

ABO manages issues via configured external issue trackers (e.g., GitHub) and an internal `WorkflowEngine`:

- **Issue Tracker**: Issues are created and tracked in the configured external system (GitHub or filesystem-based). The `ManagerAgent` retrieves open issues via `list_issues` and `get_open_work`.
- **Workflow Engine** (`WorkflowEngine.cs`): Determines the current workflow step from the issue's state/labels and resolves the required role and next transitions.
- **Environments** (`environments.json`): Maps environment names to local directory paths, issue tracker configurations, and wiki configurations. Used by the connector for path resolution and API access.
- The current issue status is accessible via the REST endpoint `GET /api/issues/{id}/status`.

---

## Secure Connector (SpecialistAgent)

The `SpecialistAgent` uses a **connector pattern** for secure filesystem and network access:

1. `ManagerAgent` calls `SpecialistAgent.InitializeWorkspaceAsync()` automatically before the agent loop starts.
2. ABO resolves the configured **environment** (`ConnectorEnvironment`) from `environments.json`.
3. A `LocalWorkspaceConnector` is instantiated and bound to the environment's directory.
4. All connector tools are mounted: filesystem, shell, issue tracker, and wiki tools (subject to the role's `AllowedTools` filter).
5. After the task is completed (when the `complete_task` sentinel is detected), the connector is released.

```
[ManagerAgent]
  ↓ InitializeWorkspaceAsync(issueId)  [called automatically before specialist loop]
[ABO-Core: Environment Resolution]
  ↓ ConnectorEnvironment { Dir = "C:\src\issue" }
[LocalWorkspaceConnector]
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
| `list_issues` | `ListIssuesTool` | List open issues from the configured issue tracker |
| `get_issue` | `GetIssueTool` | Retrieve a specific issue by ID |
| `create_issue` | `CreateIssueTool` | Create a new issue in the issue tracker |
| `add_issue_comment` | `AddIssueCommentTool` | Add a comment to an existing issue |
| `get_wiki_page` | `GetWikiPageTool` | Retrieve the contents of a wiki page |
| `create_wiki_page` | `CreateWikiPageTool` | Create a new wiki page |
| `update_wiki_page` | `UpdateWikiPageTool` | Update an existing wiki page |
| `move_wiki_page` | `MoveWikiPageTool` | Move (and optionally rename) an existing wiki page |
| `search_wiki` | `SearchWikiTool` | Search the configured wiki |

### HttpGet Security Architecture

The `http_get` tool delegates to `LocalWorkspaceConnector.HttpGetAsync`, which enforces a multi-layered security model via the separate `HttpGetSecurityHelper` class:

```
[HttpGetTool.ExecuteAsync]
  ↓ URL-Parsing + Parameter-Validation
[LocalWorkspaceConnector.HttpGetAsync]
  ↓ Schema-Check (http/https only)
  ↓ SSRF-Check: HttpGetSecurityHelper.CheckSsrfAsync(uri)
    ├── Loopback-Hostname-Block (localhost, 127.0.0.1, [::1])
    ├── Direct IP: IPAddress.IsLoopback() + IsPrivateIpAddress()
    └── DNS-Resolution: all addresses checked (RFC-1918 bitmasks)
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
| `/api/issues/{id}/status` | GET | Returns the current workflow step and status of an issue |
| `/api/interact` | POST | Main chat endpoint: receives messages, selects agent, returns response |
| `/api/issues` | GET | Lists all active issues (name, ID, status) |
| `/api/llm-consumption` | GET | Returns LLM consumption statistics (supports `?limit=N`) |
| `/api/llm-traffic` | GET | Returns the LLM traffic log for debugging |
| `/api/open-work` | GET | Returns currently open work items across all active issues |
| `/api/sessions` | GET | Returns active agent sessions with history length and timestamps |
| `/` | GET | Web UI (`wwwroot/index.html`) – Chat interface |
| `/agents/index.html` | GET | Agent / session overview |
| `/open-work/index.html` | GET | Open work dashboard |
| `/llm-traffic/index.html` | GET | LLM traffic log viewer |
| `/llm-stats/index.html` | GET | LLM consumption statistics dashboard |

Detailed API documentation with request/response schemas: see [services.md](services.md).

---

## Runtime Data Structure (`/Data/`)

The `/Data/` directory contains ABO runtime data. These files are written and read at runtime and should **not** be checked into version control.

```
/Data
  /Environments/
    environments.json     - Configured connector environments (name → directory path,
                            issue tracker config, wiki config)
  users.json              - All users (Mattermost ID, username, roles)
  llm_traffic.jsonl       - LLM requests/responses (debugging, JSONL format)
  llm_consumption.jsonl   - LLM token/cost tracking per agent run (JSONL format)
```

### Key Data Files in Detail

| File | Description |
|---|---|
| `environments.json` | Maps environment names (e.g. `abo`) to local directory paths, issue tracker type/config, and wiki type/config. Used by the connector for path and API resolution. |
| `users.json` | Persisted user data: Mattermost ID, username, roles array. |
| `llm_traffic.jsonl` | Debug log: every AI API request and response is logged as a JSON line. Useful for error analysis. Can grow large under heavy usage. |
| `llm_consumption.jsonl` | Token and cost tracking per agent run (prompt tokens, completion tokens, total tokens, cost in USD, session ID). |

---

## Directory Structure

```
/Abo.Core
  /Core
    /Connectors   - IConnector, LocalWorkspaceConnector, HttpGetSecurityHelper,
                    ConnectorEnvironment, IssueTracker/Wiki connector interfaces
    AgentSentinels.cs
    AvailableRoles.cs
    WorkflowEngine.cs

/Abo.Pm
  /Agents         - Agent implementations (IAgent): ManagerAgent, SpecialistAgent
  /Core
    AgentSupervisor.cs
    Orchestrator.cs
    SessionService.cs
  /Contracts      - JSON schemas and DTOs for API interaction
  /Data
    /Environments - environments.json
  /Docs           - This documentation
  /Integrations
    /GitHub       - GitHub issue tracker connector + HTTP client
    /Mattermost   - Mattermost HTTP client + WebSocket listener
    /XpectoLive   - XpectoLive HTTP client + Wiki client
  /Models         - Database models / entities (User)
  /Services       - Business logic services (CronjobAutoStartService,
                    EnvironmentValidationService, StartupStatusService)
  /Tools          - IAboTool implementations (global tools)
    /Connector    - Connector tools (ReadFileTool, WriteFileTool, GitTool, DotnetTool,
                    PythonTool, SearchRegexTool, HttpGetTool, MoveWikiPageTool, etc.)
  /wwwroot        - Static Web UI files (Chat, Agents, Open Work,
                    LLM Traffic, LLM Stats dashboards)
```
