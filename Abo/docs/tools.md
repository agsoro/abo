# Tools and Plugins

Tools in ABO are local C# methods that act as plugins and can be triggered by the AI model via JSON-based tool calls. They are located in the `/Tools` directory.

## Implementation Details

Since ABO uses pure .NET 10 without proprietary SDKs, tools are standard C# classes implementing the `IAboTool` interface.

### Security, Execution, and the "Tools" Array

When the AI model requests a tool call:
1. The orchestrator (or a specialized agent) serializes the available tools into the standard JSON `tools` array format.
2. The model returns a tool call payload.
3. Parameters are safely deserialized using `System.Text.Json`.
4. The corresponding C# method is safely invoked in the local environment.
5. The result is serialized and returned to the model as a tool message.

This approach deliberately avoids the complexity of the full MCP (Model Context Protocol) and guarantees that the AI has **no direct access** to databases or internal APIs.

---

## General Tools

### `get_system_time`
- **Class**: `GetSystemTimeTool`
- **Description**: Returns the current UTC system time.
- **Parameters**: none
- **Used by**: HelloWorldAgent, QuizAgent, PmoAgent, EmployeeAgent

---

## Quiz Tools

### `get_random_question`
- **Class**: `GetRandomQuestionTool`
- **Description**: Retrieves a random quiz question from the data store (optionally filtered by topic).
- **Parameters**: `topic` (optional, string)

### `ask_quiz_question`
- **Class**: `AskQuizQuestionTool`
- **Description**: Presents a multiple-choice quiz question formatted as Markdown.
- **Parameters**: `id` (string), `topic` (string), `options` (array)
- **Important**: The fields `id`, `topic`, and `options` must always be passed from the source data of the question.

### `add_quiz_question`
- **Class**: `AddQuizQuestionTool`
- **Description**: Inserts a new quiz question into the data store after explicit user confirmation.
- **Parameters**: `topic`, `question`, `options`, `answer`, `explanation`, `explanationUrl` (optional), `userId`

### `get_quiz_topics`
- **Class**: `GetQuizTopicsTool`
- **Description**: Returns all available quiz topics.
- **Parameters**: none

### `update_quiz_score`
- **Class**: `QuizTools` (score update)
- **Description**: Updates a user's score. Must **only** be called for a correct answer.
- **Parameters**: `channelId`, `userName`, `topic` (optional)

### `get_quiz_leaderboard`
- **Class**: `QuizTools` (leaderboard)
- **Description**: Returns the current quiz leaderboard.
- **Parameters**: `channelId`

### `subscribe_quiz` / `unsubscribe_quiz`
- **Class**: `QuizTools`
- **Description**: Manages hourly quiz subscriptions for a channel.
- **Parameters**: `channelId`, `userName`

### `ask_multiple_choice`
- **Class**: `AskMultipleChoiceTool`
- **Description**: Asks a generic multiple-choice question (e.g. favorite color). Used by the `HelloWorldAgent`.
- **Parameters**: `question`, `options` (array)

---

## PMO / Process Management Tools

### `create_process`
- **Class**: `CreateProcessTool`
- **Description**: Creates a new BPMN process definition as a `.bpmn` file. Automatically validates the XML before saving.
- **Parameters**: `processId` (string, unique), `bpmnXml` (string, complete BPMN 2.0 XML)
- **Important**: Every node, gateway, and transition **must have a unique ID**.

### `update_process`
- **Class**: `UpdateProcessTool`
- **Description**: Updates an existing BPMN process definition.
- **Parameters**: `processId`, `bpmnXml`

### `check_bpmn`
- **Class**: `CheckBpmnTool`
- **Description**: Checks whether a BPMN XML string is well-formed and parseable. Should be used **before saving** via `create_process` or `update_process`.
- **Parameters**: `bpmnXml`

### `start_project`
- **Class**: `StartProjectTool`
- **Description**: Starts a new project instance based on an existing BPMN process. Creates the project directory, `info.md`, `status.json`, and registers the project in `active_projects.json`.
- **Parameters**: `projectId`, `title`, `typeId`, `info`, `initialStepId`, `environmentName`, `parentId` (optional)

### `list_projects`
- **Class**: `ListProjectsTool`
- **Description**: Lists all active projects with hierarchy, type, current BPMN step, and status link.
- **Parameters**: none

### `get_open_work`
- **Class**: `GetOpenWorkTool`
- **Description**: Analyzes all active projects and extracts structured, actionable tasks. Shows expected role and state based on the BPMN flow.
- **Parameters**: none

### `upsert_role`
- **Class**: `UpsertRoleTool`
- **Description**: Creates or updates an AI agent role (name + system prompt). Role IDs must match those used in the BPMN.
- **Parameters**: `roleId`, `name`, `systemPrompt`

### `get_roles`
- **Class**: `GetRolesTool`
- **Description**: Returns all currently defined AI roles and their descriptions.
- **Parameters**: none

### `get_environments`
- **Class**: `GetEnvironmentsTool`
- **Description**: Lists all configured environments available for projects (e.g. local directories with name, type, and path).
- **Parameters**: none

---

## Connector Tools (EmployeeAgent)

These tools are **only available after a successful `checkout_project`**. All paths are confined to the checked-out project environment's directory. They are located in the `/Tools/Connector` subfolder.

### `checkout_project`
- **Description**: Checks out a running project by its ID and binds the environment connector to it. Required before any filesystem or shell tools.
- **Parameters**: `projectId` (string)
- **Implemented in**: `EmployeeAgent.HandleCheckoutProjectAsync` (no separate tool file)

### `complete_task`
- **Description**: Marks the current task in the checked-out project as completed and updates the status.
- **Parameters**: `nextStepId` (optional, string) – ID of the next BPMN step
- **Implemented in**: `EmployeeAgent.HandleCompleteTaskAsync` (no separate tool file)

### `request_ceo_help`
- **Description**: Stops work and escalates a question/issue to the human CEO.
- **Parameters**: `message` (string)
- **Implemented in**: `EmployeeAgent.HandleRequestCeoHelp` (no separate tool file)

### `read_file`
- **Class**: `ReadFileTool` (`/Tools/Connector/ReadFileTool.cs`)
- **Description**: Reads the contents of a file using a relative path.
- **Parameters**: `relativePath`

### `write_file`
- **Class**: `WriteFileTool` (`/Tools/Connector/WriteFileTool.cs`)
- **Description**: Writes content to a file (creates or overwrites).
- **Parameters**: `relativePath`, `content`

### `delete_file`
- **Class**: `DeleteFileTool` (`/Tools/Connector/DeleteFileTool.cs`)
- **Description**: Deletes a file from the project directory.
- **Parameters**: `relativePath`

### `list_dir`
- **Class**: `ListDirTool` (`/Tools/Connector/ListDirTool.cs`)
- **Description**: Lists the contents of a directory.
- **Parameters**: `relativePath`

### `mkdir`
- **Class**: `MkDirTool` (`/Tools/Connector/MkDirTool.cs`)
- **Description**: Creates a new directory in the project.
- **Parameters**: `relativePath`

### `git`
- **Class**: `GitTool` (`/Tools/Connector/GitTool.cs`)
- **Description**: Executes a git command in the project directory. The word `git` must **not** be included in the arguments.
- **Parameters**: `arguments`

### `dotnet`
- **Class**: `DotnetTool` (`/Tools/Connector/DotnetTool.cs`)
- **Description**: Executes a .NET CLI command in the project directory. The word `dotnet` must **not** be included in the arguments.
- **Parameters**: `arguments`
