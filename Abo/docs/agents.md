# Agent Overview

ABO supports a specialized multi-agent architecture. Instead of a single monolithic bot, a **Supervisor** coordinates specialized agents.

## Agent Supervisor

The `AgentSupervisor` is the entry point for all user interactions. It uses the LLM to analyze the user's intent and selects the most appropriate agent based on each agent's `Name` and `Description`.

## Registered Agents

Agents in ABO are specialized roles with specific instructions, tools, and constraints. All agents implement the `IAgent` interface.

---

### HelloWorldAgent
- **Class**: `Abo.Agents.HelloWorldAgent`
- **Description**: A simple assistant for general greetings, time queries, and basic tests.
- **Requires Capable Model**: No (`RequiresCapableModel = false`)
- **Tools**:
  - `get_system_time` – Returns the current system time.
  - `ask_multiple_choice` – Asks multiple-choice questions about personal preferences (e.g. favorite color).
- **Usage**: Selected when the user asks for the time or makes general greetings.
- **Note**: This agent is **not** a quiz agent. If quiz requests mistakenly arrive here, they will not be handled.

---

### QuizAgent
- **Class**: `Abo.Agents.QuizAgent`
- **Description**: A specialized agent for tech and nerd trivia, subscriptions, and leaderboards.
- **Requires Capable Model**: No (`RequiresCapableModel = false`)
- **Tools**:
  - `get_random_question` – Retrieves a random quiz question from the data store (optionally filtered by topic).
  - `ask_quiz_question` – Presents a question to the user (formatted Markdown with `id`, `topic`, and `options`).
  - `add_quiz_question` – Adds a new quiz question after explicit user confirmation.
  - `get_quiz_topics` – Returns all available topic areas.
  - `update_quiz_score` – Updates the score **only for a correct answer**.
  - `get_quiz_leaderboard` – Displays the current leaderboard.
  - `subscribe_quiz` / `unsubscribe_quiz` – Manages hourly quiz subscriptions for a channel.
  - `get_system_time` – Returns the current system time.
- **Rules**:
  - Points are awarded **exclusively for correct answers**. `update_quiz_score` must never be called for a wrong answer.
  - A comprehensible explanation is **mandatory** for every answer (including a link if `explanationUrl` is available).
  - New questions are **only saved after explicit user confirmation** (no automatic saving).
  - There is no `check_quiz_answer` tool – answer evaluation is done by the model itself based on the conversation history.
  - When calling `add_quiz_question`, the **Mattermost User ID** from the `[CONTEXT]` must be passed as `userId`.
  - **Channel ID** and **User Name** from the `[CONTEXT]` must be used for all tools.

---

### PmoAgent (Project Management Office)
- **Class**: `Abo.Agents.PmoAgent`
- **Description**: The PMO Lead agent. Responsible for designing BPMN processes, instantiating projects, and managing roles.
- **Requires Capable Model**: Yes (`RequiresCapableModel = true`)
- **Tools**:
  - `create_process` – Creates a new BPMN process definition.
  - `update_process` – Updates an existing BPMN process definition.
  - `start_project` – Starts a new project instance based on an existing process.
  - `list_projects` – Lists all active projects and their current status.
  - `get_open_work` – Shows open work items across all projects.
  - `upsert_role` – Creates or updates an AI agent role with a system prompt.
  - `get_roles` – Lists all defined roles.
  - `get_system_time` – Returns the current system time.
- **Approach**: Follows the PDCA cycle (Plan → Do → Check → Act). Designs processes, defines roles, and delegates execution work to the `EmployeeAgent`.
- **Rules**:
  - Every node, gateway, and transition in the BPMN **must have a unique ID**.
  - Always check `get_roles` before creating new roles.
  - The PMO agent does **not** perform direct task work – it delegates to instantiated BPMN flows.
  - Users can visualize processes in the Web UI at `/processes/index.html`.

---

### EmployeeAgent
- **Class**: `Abo.Agents.EmployeeAgent`
- **Description**: The generic worker agent. Takes on concrete tasks from running projects and executes them autonomously.
- **Requires Capable Model**: Yes (`RequiresCapableModel = true`)
- **Lifecycle Tools**:
  - `checkout_project` – Binds a secure connector to a project environment (must be called before filesystem/shell tools).
  - `complete_task` – Marks the current task as completed and advances the BPMN step. Optional parameter: `nextStepId`.
  - `request_ceo_help` – Escalates an issue to the human CEO.
- **Global Information Tools**: `list_projects`, `get_open_work`, `get_system_time`, `get_roles`, `get_environments`
- **Connector Tools** (only available after `checkout_project`):
  - `read_file` – Read a file.
  - `write_file` – Write/create a file.
  - `delete_file` – Delete a file.
  - `list_dir` – List directory contents.
  - `mkdir` – Create a new directory.
  - `git` – Execute git commands (without the word `git`).
  - `dotnet` – Execute .NET CLI commands (without the word `dotnet`).
- **Security**: All filesystem and shell operations are confined to the checked-out project environment's directory. Paths outside are not accessible.
- **Workflow**:
  1. Call `list_projects` or `get_open_work` to find open work.
  2. Call `checkout_project` with the `projectId`.
  3. Read the project role and task from `info.md` or BPMN.
  4. Perform work using connector tools.
  5. Call `complete_task` (optionally with `nextStepId`).

## Implementation

All agents implement the `IAgent` interface (`Abo.Agents.IAgent`) and are registered as transient services in `Program.cs`. The `AgentSupervisor` dynamically selects the appropriate agent via LLM-based intent analysis using the `Name` and `Description` of each agent.
