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

### ManagerAgent
- **Class**: `Abo.Agents.ManagerAgent`
- **Description**: The Issue Lead / Manager. Identifies open tasks from active issues and delegates them to specialized agents who do the actual work.
- **Requires Capable Model**: Yes (`RequiresCapableModel = true`)
- **Tools**:
  - `list_issues` – Lists all active issues and their current status.
  - `get_open_work` – Shows open work items across all issues.
  - `get_roles` – Lists all defined roles.
  - `get_system_time` – Returns the current system time.
  - `delegate_task` – Assigns specific work to a `SpecialistAgent` and executes the sub-agent workflow.
- **Workflow**:
  1. Use `get_open_work` to identify active issues needing work.
  2. Determine the required role using `get_roles`.
  3. Call `delegate_task` with `issueId`.

---

### SpecialistAgent
- **Class**: `Abo.Agents.SpecialistAgent`
- **Description**: The generic worker agent. Takes on concrete tasks delegated by the `ManagerAgent` and executes them autonomously in a specialized role.
- **Requires Capable Model**: Yes (`RequiresCapableModel = true`)
- **Lifecycle Tools**:
  - `checkout_task` – Binds a secure connector to a issue environment (must be called before filesystem/shell tools).
  - `complete_task` – Marks the current task as completed and advances the BPMN step. Optional parameter: `nextStepId`.
  - `request_ceo_help` – Escalates an issue to the human CEO.
  - `take_notes` – Stores temporary notes, remarks, or intermediate findings during tasks.
  - `read_notes` – Reads the temporary notes stored for the current issue.
- **Global Information Tools**: `get_system_time`, `get_environments`
- **Connector Tools** (only available after `checkout_task`):
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
  - `search_wiki` – Search the configured wiki.
- **Security**: All filesystem and shell operations are confined to the checked-out issue environment's directory. Paths outside are not accessible.
- **Workflow**:
  1. The agent is instantiated with a specific role and prompt by the `ManagerAgent`.
  2. Call `checkout_task` with the `issueId` provided in the instructions.
  3. Perform work using connector tools based on instructions.
  4. Call `complete_task` (optionally with `nextStepId`).

## Implementation

All primary agents implement the `IAgent` interface (`Abo.Agents.IAgent`) and are registered as transient services in `Program.cs`. The `AgentSupervisor` dynamically selects the appropriate agent via LLM-based intent analysis using the `Name` and `Description` of each agent. `SpecialistAgent` is instantiated dynamically by `ManagerAgent` and is not available to the `AgentSupervisor`.
