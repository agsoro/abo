# Agent Overview

ABO supports a specialized multi-agent architecture. Instead of a single monolithic bot, a **Supervisor** coordinates specialized agents.

## Agent Supervisor

The `AgentSupervisor` is the entry point for all user interactions. It uses the LLM to analyze the user's intent and selects the most appropriate agent based on each agent's `Name` and `Description`.

## Registered Agents

Agents in ABO are specialized roles with specific instructions, tools, and constraints. All agents implement the `IAgent` interface.

---

### ManagerAgent
- **Class**: `Abo.Agents.ManagerAgent`
- **Description**: The Issue Lead / Manager. Identifies open tasks from active issues and delegates them to specialized agents who do the actual work.
- **Requires Capable Model**: No (`RequiresCapableModel = false`)
- **Requires Review Model**: No (`RequiresReviewModel = false`)
- **Tools**:
  - `list_issues` – Lists all active issues and their current status.
  - `get_open_work` – Shows open work items across all issues, sorted by priority.
  - `delegate_task` – Assigns specific work to a `SpecialistAgent` and executes the sub-agent workflow.
- **Workflow**:
  1. Use `get_open_work` to identify active issues needing work (sorted by priority: `open` > `review` > `check` > `work` > `planned`).
  2. Call `delegate_task` with `issueId` to hand off work to a `SpecialistAgent`.
  3. The `delegate_task` tool executes the specialist synchronously and terminates the manager loop upon completion.

---

### SpecialistAgent
- **Class**: `Abo.Agents.SpecialistAgent`
- **Description**: The generic worker agent. Takes on concrete tasks delegated by the `ManagerAgent` and executes them autonomously in a specialized role.
- **Requires Capable Model**: Yes (`RequiresCapableModel = true`)
- **Requires Review Model**: Yes, dynamically — `true` if the role title contains "review", "qa", "test", or "validation" (case-insensitive).
- **Lifecycle Tools**:
  - `complete_task` – Marks the current task as completed and advances the workflow step. Required parameters: `resultNotes` (string, **required**), `keyword` (string, optional — used when the step leads to a decision gateway with multiple possible paths). See [Sentinel-Based Loop Termination](#sentinel-based-loop-termination) below.
  - `request_ceo_help` – Escalates an issue to the human CEO.
- **Connector Tools** (automatically available after workspace initialization):
  - `read_file` – Read a file.
  - `write_file` – Write/create a file.
  - `delete_file` – Delete a file.
  - `list_dir` – List directory contents.
  - `mkdir` – Create a new directory.
  - `git` – Execute git commands (without the word `git`).
  - `dotnet` – Execute .NET CLI commands (without the word `dotnet`).
  - `python` – Execute Python commands (without the word `python`).
  - `search_regex` – Search for a regex pattern across files and filenames within a directory.
  - `http_get` – Execute an HTTP GET request to external endpoints.
  - `list_issues` – List open issues or features from the issue's Issue Tracker.
  - `get_issue` – Retrieve a specific issue by ID.
  - `create_issue` – Create a new issue, feature, or bug.
  - `add_issue_comment` – Add a comment to an existing issue.
  - `get_wiki_page` – Retrieve the contents of a wiki page (local or external).
  - `create_wiki_page` – Create a new wiki page.
  - `update_wiki_page` – Update an existing wiki page.
  - `move_wiki_page` – Move (and optionally rename) an existing wiki page to a new parent location.
  - `search_wiki` – Search the configured wiki.
- **Security**: All filesystem and shell operations are confined to the checked-out issue environment's directory. Paths outside are not accessible.
- **Workflow**:
  1. The `ManagerAgent` instantiates the `SpecialistAgent` with the appropriate role and prompt, then automatically calls `InitializeWorkspaceAsync()` to bind the connector before the agent loop starts.
  2. The agent receives the `issueId` in its initial message and performs work using connector tools.
  3. When work is complete, the agent calls `complete_task` with `resultNotes` (and optionally `keyword` for decision gateways).

---

## Implementation

All primary agents implement the `IAgent` interface (`Abo.Agents.IAgent`) and are registered as transient services in `Program.cs`. The `AgentSupervisor` dynamically selects the appropriate agent via LLM-based intent analysis using the `Name` and `Description` of each agent. `SpecialistAgent` is instantiated dynamically by `ManagerAgent` and is not directly available to the `AgentSupervisor`.

---

## Roles

Roles define the title, system prompt, and allowed connector tools for a `SpecialistAgent` instance. They are defined in `Abo.Core/Core/AvailableRoles.cs` (authoritative source).

| RoleId | Title | Allowed Tools |
|---|---|---|
| `Role_Productmanager` | Product Manager | `list_issues`, `get_issue`, `add_issue_comment`, `get_wiki_page`, `read_file`, `list_dir`, `search_wiki` |
| `Role_Architect` | Software Architect | `read_file`, `list_dir`, `search_regex`, `get_issue`, `add_issue_comment`, `get_wiki_page`, `create_wiki_page`, `update_wiki_page`, `search_wiki` |
| `Role_Developer` | Developer | `read_file`, `write_file`, `delete_file`, `list_dir`, `mkdir`, `git`, `dotnet`, `python`, `search_regex`, `http_get`, `get_issue`, `add_issue_comment`, `get_wiki_page`, `update_wiki_page`, `search_wiki` |
| `Role_QA` | QA Engineer | `read_file`, `list_dir`, `git`, `dotnet`, `python`, `search_regex`, `http_get`, `get_issue`, `add_issue_comment`, `get_wiki_page`, `create_wiki_page`, `update_wiki_page`, `search_wiki` |
| `Role_Releaseengineer` | Release Engineer | `read_file`, `list_dir`, `git`, `get_issue`, `add_issue_comment` |

---

## Global Tools

Global tools are shared C# services registered in `Program.cs` and available to all agents (subject to role restrictions).

| Tool | Class | Description |
|---|---|---|
| `get_system_time` | `GetSystemTimeTool` | Returns the current UTC system time. |
| `start_issue` | `StartIssueTool` | Starts a new issue instance. |
| `list_issues` | `ListActiveIssuesTool` | Lists all active issues across all configured environments. |
| `get_environments` | `GetEnvironmentsTool` | Lists all configured connector environments. |
| `get_open_work` | `GetOpenWorkTool` | Returns structured, prioritized open work items. |

---

## Sentinel-Based Loop Termination

The agent loop uses a sentinel string convention for clean termination without extra LLM round-trips:

- **`complete_task` sentinel** (`AgentSentinels.CompleteTaskResult` = `[COMPLETE_TASK_RESULT]:`):
  - `SpecialistAgent.HandleCompleteTaskAsync` returns the sentinel prefix followed by `resultNotes`.
  - The `Orchestrator.RunAgentLoopAsync` detects this prefix and immediately returns `resultNotes` to the caller.
  - **No extra LLM synthesis call is made** — the user sees the agent's `resultNotes` directly.
  - `resultNotes` are also persisted as an issue comment before the sentinel is returned.

- **`delegate_task` sentinel** (`[TERMINATE_MANAGER_LOOP]`):
  - `ManagerAgent.HandleDelegateTaskAsync` returns this prefix after the specialist completes.
  - The `Orchestrator` ends the manager's agent loop.
