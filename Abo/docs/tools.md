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

## Connector Tools (SpecialistAgent)

These tools are **only available after a successful `checkout_task`**. All paths are confined to the checked-out project environment's directory. They are located in the `/Tools/Connector` subfolder.

### `checkout_task`
- **Description**: Checks out a running project by its ID and binds the environment connector to it. Required before any filesystem or shell tools.
- **Parameters**: `projectId` (string)
- **Implemented in**: `SpecialistAgent.HandleCheckoutTaskAsync` (no separate tool file)

### `complete_task`
- **Description**: Marks the current task in the checked-out project as completed and updates the status.
- **Parameters**: `nextStepId` (optional, string) – ID of the next BPMN step
- **Implemented in**: `SpecialistAgent.HandleCompleteTaskAsync` (no separate tool file)

### `request_ceo_help`
- **Description**: Stops work and escalates a question/issue to the human CEO.
- **Parameters**: `message` (string)
- **Implemented in**: `SpecialistAgent.HandleRequestCeoHelp` (no separate tool file)

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

### `python`
- **Class**: `PythonTool` (`/Tools/Connector/PythonTool.cs`)
- **Description**: Runs a Python command in the project directory. The word `python` must **not** be included in the arguments. Uses the system's `python` executable, which must be installed and available in the system PATH.
- **Parameters**: `arguments` (string) – e.g. `script.py`, `-m pytest`, `-m venv .venv`, `-m pip install -r requirements.txt`
- **Examples**:
  - Run a script: `arguments = "main.py"`
  - Install dependencies: `arguments = "-m pip install -r requirements.txt"`
  - Run tests with pytest: `arguments = "-m pytest"`
  - Create a virtual environment: `arguments = "-m venv .venv"`
- **Implemented in**: `LocalWindowsConnector.RunPythonAsync` → delegates to `RunProcessAsync("python", arguments)`
- **Note**: The working directory is always set to the checked-out project's environment directory.

### `search_regex`
- **Class**: `SearchRegexTool` (`/Tools/Connector/SearchRegexTool.cs`)
- **Description**: Searches for a regex pattern within filenames and file contents across the specified directory. Useful for finding code patterns, usages, or content across multiple files.
- **Parameters**:
  - `searchPath` (string) – Relative path to search within. Use `'.'` or empty string for the project root.
  - `pattern` (string) – A valid .NET regular expression pattern.
  - `limitLinesPerFile` (integer, optional) – Maximum number of matching lines returned per file. Defaults to `10`.
- **Returns**: A list of files with matching lines (file path + line number + content). If a filename itself matches the pattern, it is also flagged.
- **Limits**: Results per file are capped at `limitLinesPerFile` lines and at 10 KB of output per file to prevent token overload.
- **Implemented in**: `LocalWindowsConnector.SearchRegexAsync`

### `http_get` *(neu – ABO-XXXX)*
- **Class**: `HttpGetTool` (`/Tools/Connector/HttpGetTool.cs`)
- **Description**: Sends an HTTP GET request to the specified URL and returns the HTTP status code and response body. Use this to query external APIs, health endpoints, or fetch remote data. Response is limited to 100 KB.
- **Parameters**:
  - `url` (string, **required**) – The full URL to send the GET request to (must start with `http://` or `https://`).
  - `headers` (object, optional) – Optional HTTP headers as key-value pairs (e.g. `{ "Authorization": "Bearer token", "Accept": "application/json" }`).
  - `timeoutSeconds` (integer, optional) – Request timeout in seconds. Defaults to `30`. Maximum: `120`.
- **Returns**: Formatted plaintext string with HTTP status code, Content-Type, and response body.
  - **Success**: `HTTP 200 OK\nContent-Type: application/json\n\n{...}`
  - **Error**: `Error (HTTP 404): Not Found\nURL: ...\n\n{...}`
  - **Timeout**: `Error (Timeout): Request exceeded 30 seconds timeout.\nURL: ...`
  - **SSRF Block**: `Error (SSRF Protection): Requests to private/internal IP addresses are not allowed (RFC-1918). Host: ...`
- **Security**:
  - **SSRF Protection**: Requests to loopback (`localhost`, `127.0.0.1`, `[::1]`) and RFC-1918 private IPs (`10.x`, `172.16-31.x`, `192.168.x`, link-local `169.254.x`) are blocked.
  - **Schema Restriction**: Only `http://` and `https://` schemas are allowed. `ftp://`, `file://`, `javascript:`, etc. are rejected.
  - **Response Size Cap**: Response body is truncated at **100 KB** to prevent LLM context overflow.
  - **Header Injection**: System headers (`Host`, `Content-Length`, `Transfer-Encoding`, `Connection`, `Upgrade`, `Proxy-Authorization`, `Proxy-Connection`) cannot be overridden by callers.
  - **Timeout Cap**: Maximum timeout is capped at **120 seconds** to prevent agent-loop blocking.
- **Implemented in**: `LocalWindowsConnector.HttpGetAsync` + `HttpGetSecurityHelper` (SSRF/header logic)
- **Tests**: `Abo.Tests/HttpGetToolTests.cs` (98 tests total, covering all security scenarios)
