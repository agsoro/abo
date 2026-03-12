# ABO Architecture

The Agsoro Bot Orchestrator (ABO) is built on a **Controller-Worker** loop that ensures maximum data privacy and data sovereignty. It implements a "Native Orchestrator" pattern.

---

## The Agent Loop

The core orchestration loop ensures that the AI never has direct, uncontrolled access to internal data. All actions are mediated by the C# orchestrator:

1. **Intelligent Selection**: When a request comes in, the `AgentSupervisor` uses the LLM to analyze the intent and select the most appropriate specialized agent (e.g. `QuizAgent`, `HelloWorldAgent`, `PmoAgent`, or `EmployeeAgent`).
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

## Project and Process Management (PMO Layer)

ABO implements a full BPMN-based project management system:

- **Process definitions** are stored as `.bpmn` files in `/Data/Processes/`.
- **Project instances** are managed in `/Data/Projects/{projectId}/`, consisting of:
  - `info.md` – Project goals, context, and initial parameters.
  - `status.json` – Current BPMN step, status, and timestamps.
- **`active_projects.json`** is the central list of all running projects (in `/Data/Projects/`).
- The `PmoAgent` designs processes and roles; the `EmployeeAgent` performs the actual work.
- The current project status is accessible via the REST endpoint `GET /api/projects/{id}/status`.

---

## Secure Connector (EmployeeAgent)

The `EmployeeAgent` uses a **connector pattern** for secure filesystem access:

1. The agent calls `checkout_project` with a `projectId`.
2. ABO resolves the configured **environment** (`ConnectorEnvironment`) of the project (stored in `environments.json`).
3. A `LocalWindowsConnector` is instantiated and bound to the environment's directory.
4. All subsequent filesystem and shell tools (`read_file`, `write_file`, `git`, `dotnet`, etc.) are **confined** to that directory.
5. After the task is completed (`complete_task`), the connector is released.

```
[EmployeeAgent]
  ↓ checkout_project(projectId)
[ABO-Core: Environment Resolution]
  ↓ ConnectorEnvironment { Dir = "C:\src\project" }
[LocalWindowsConnector]
  ↓ read_file / write_file / git / dotnet ...
[Filesystem (confined to Dir)]
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
| `/api/projects/{id}/status` | GET | Returns the current BPMN step and status of a project |
| `/api/interact` | POST | Main chat endpoint: receives messages, selects agent, returns response |
| `/` | GET | Web UI (`wwwroot/index.html`) |
| `/processes/index.html` | GET | BPMN process viewer |

Detailed API documentation with request/response schemas: see [services.md](services.md).

---

## Runtime Data Structure (`/Data/`)

The `/Data/` directory contains all ABO runtime data. These files are written and read at runtime and should **not** be checked into version control (exception: initial data such as BPMN processes).

```
/Data
  /Processes/             - BPMN process definitions (.bpmn files)
  /Projects/
    active_projects.json  - Central list of all active projects
    /{projectId}/
      info.md             - Project goals, context, initial parameters
      status.json         - Current BPMN step, status, timestamps
      notes.md            - Handover notes between agents/steps
  /Environments/
    environments.json     - Configured connector environments (name → directory path)
  /Quiz/
    leaderboard.json      - Quiz leaderboard with scores
  users.json              - All users (Mattermost ID, username, roles, quiz subscription)
  llm_traffic.jsonl       - LLM requests/responses (debugging, JSONL format)
```

### Key Data Files in Detail

| File | Description |
|---|---|
| `active_projects.json` | JSON array of all running project IDs and their process type. Managed by `PmoAgent`. |
| `{projectId}/status.json` | Contains `ProjectId`, `CurrentStepId`, `Status`, and `LastUpdated`. Updated on every `complete_task`. |
| `{projectId}/info.md` | Created when a project is started; describes goal, context, and parameters. Immutable after creation. |
| `{projectId}/notes.md` | Handover notes: each agent writes result context here for the next step. |
| `environments.json` | Maps environment names (e.g. `abo`) to local directory paths. Used by the connector for path resolution. |
| `users.json` | Persisted user data: Mattermost ID, username, roles array, quiz subscription flag. |
| `leaderboard.json` | Quiz system scores, indexed by username. |
| `llm_traffic.jsonl` | Debug log: every AI API request and response is logged as a JSON line. Useful for error analysis. Can grow large under heavy usage. |

---

## Directory Structure

```
/Abo
  /Agents         - Agent implementations (IAgent)
  /Core
    /Connectors   - IConnector, LocalWindowsConnector, ConnectorEnvironment
    AgentSupervisor.cs
    Orchestrator.cs
    SessionService.cs
  /Contracts      - JSON schemas and DTOs for API interaction
  /Data
    /Processes    - BPMN process definitions (.bpmn)
    /Projects     - Project instances (info.md, status.json, active_projects.json)
    /Environments - environments.json
    /Quiz         - Quiz data (leaderboard.json)
  /Docs           - This documentation
  /Integrations
    /Mattermost   - Mattermost HTTP client + WebSocket listener
    /XpectoLive   - XpectoLive HTTP client + Wiki client
  /Models         - Database models / entities (User)
  /Services       - Business logic services (UserService, QuizService)
  /Tools          - IAboTool implementations
    /Connector    - Connector tools (ReadFileTool, GitTool, ...)
  /wwwroot        - Static Web UI files (BPMN viewer, Chat UI)
```
