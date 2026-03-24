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

## Global / PMO Tools

These tools are registered as global services in `Program.cs` and are available to `ManagerAgent` (subject to the allowed-tools filter).

### `get_system_time`
- **Class**: `GetSystemTimeTool`
- **Description**: Returns the current UTC system time.
- **Parameters**: none

### `start_issue`
- **Class**: `StartIssueTool`
- **Description**: Starts a new issue instance based on a workflow definition. Creates the necessary records and registers the issue as active.
- **Parameters**: `issueId`, `title`, `typeId`, `info`, `initialStepId`, `environmentName`, `parentId` (optional)

### `list_issues`
- **Class**: `ListActiveIssuesTool`
- **Description**: Lists all active issues with hierarchy, type, current workflow step, and status information.
- **Parameters**: none

### `get_open_work`
- **Class**: `GetOpenWorkTool`
- **Description**: Analyzes all active issues and extracts structured, actionable tasks. Shows expected role and state based on the workflow.
- **Parameters**: none

### `get_environments`
- **Class**: `GetEnvironmentsTool`
- **Description**: Lists all configured environments available for issues (e.g. local directories with name, type, and path).
- **Parameters**: none

---

## Connector Tools (SpecialistAgent)

These tools are **automatically mounted** during workspace initialization (`InitializeWorkspaceAsync`), which is called by `ManagerAgent` before the specialist agent loop starts. All paths are confined to the checked-out issue environment's directory. They are located in the `/Tools/Connector` subfolder.

> **Note**: Which tools are actually available to the agent depends on the role's `AllowedTools` list defined in `Abo.Core/Core/AvailableRoles.cs`.

### Lifecycle Tools

#### `complete_task`
- **Description**: Marks the current task in your checked-out issue as completed and updates its status. This will also terminate your session.
- **Parameters**:
  - `resultNotes` (string, **required**) – A detailed summary of the work performed, results, and any context needed by the next agent. These notes are automatically added as a comment to the issue.
  - `keyword` (string, optional) – If the current step leads to a decision gateway with multiple possible next steps, provide a keyword matching the condition name of the path to take.
- **Behavior**: Uses the `AgentSentinels.CompleteTaskResult` (`[COMPLETE_TASK_RESULT]:`) sentinel. The `resultNotes` are returned **directly** to the caller (no extra LLM synthesis round-trip). The orchestrator detects the sentinel prefix and short-circuits the loop.
- **Implemented in**: `SpecialistAgent.HandleCompleteTaskAsync`

#### `request_ceo_help`
- **Description**: Stops work and escalates a question/issue to the human CEO.
- **Parameters**: `message` (string)
- **Implemented in**: `SpecialistAgent.HandleRequestCeoHelp`

---

### Filesystem Tools

#### `read_file`
- **Class**: `ReadFileTool` (`/Tools/Connector/ReadFileTool.cs`)
- **Description**: Reads the contents of a file using a relative path.
- **Parameters**: `relativePath`

#### `write_file`
- **Class**: `WriteFileTool` (`/Tools/Connector/WriteFileTool.cs`)
- **Description**: Writes content to a file (creates or overwrites).
- **Parameters**: `relativePath`, `content`

#### `delete_file`
- **Class**: `DeleteFileTool` (`/Tools/Connector/DeleteFileTool.cs`)
- **Description**: Deletes a file from the issue directory.
- **Parameters**: `relativePath`

#### `list_dir`
- **Class**: `ListDirTool` (`/Tools/Connector/ListDirTool.cs`)
- **Description**: Lists the contents of a directory.
- **Parameters**: `relativePath`

#### `mkdir`
- **Class**: `MkDirTool` (`/Tools/Connector/MkDirTool.cs`)
- **Description**: Creates a new directory in the issue.
- **Parameters**: `relativePath`

#### `search_regex`
- **Class**: `SearchRegexTool` (`/Tools/Connector/SearchRegexTool.cs`)
- **Description**: Searches for a regex pattern within filenames and file contents across the specified directory. Useful for finding code patterns, usages, or content across multiple files.
- **Parameters**:
  - `searchPath` (string) – Relative path to search within. Use `'.'` or empty string for the issue root.
  - `pattern` (string) – A valid .NET regular expression pattern.
  - `limitLinesPerFile` (integer, optional) – Maximum number of matching lines returned per file. Defaults to `10`.
- **Returns**: A list of files with matching lines (file path + line number + content). If a filename itself matches the pattern, it is also flagged.
- **Limits**: Results per file are capped at `limitLinesPerFile` lines and at 10 KB of output per file to prevent token overload.

---

### Shell Tools

#### `git`
- **Class**: `GitTool` (`/Tools/Connector/GitTool.cs`)
- **Description**: Executes a git command in the issue directory. The word `git` must **not** be included in the arguments.
- **Parameters**: `arguments`

#### `dotnet`
- **Class**: `DotnetTool` (`/Tools/Connector/DotnetTool.cs`)
- **Description**: Executes a .NET CLI command in the issue directory. The word `dotnet` must **not** be included in the arguments.
- **Parameters**: `arguments`

#### `python`
- **Class**: `PythonTool` (`/Tools/Connector/PythonTool.cs`)
- **Description**: Runs a Python command in the issue directory. The word `python` must **not** be included in the arguments. Uses the system's `python` executable, which must be installed and available in the system PATH.
- **Parameters**: `arguments` (string) – e.g. `script.py`, `-m pytest`, `-m venv .venv`, `-m pip install -r requirements.txt`
- **Examples**:
  - Run a script: `arguments = "main.py"`
  - Install dependencies: `arguments = "-m pip install -r requirements.txt"`
  - Run tests with pytest: `arguments = "-m pytest"`
  - Create a virtual environment: `arguments = "-m venv .venv"`
- **Note**: The working directory is always set to the checked-out issue's environment directory.

---

### HTTP Tool

#### `http_get`
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
- **Tests**: `Abo.Tests/HttpGetToolTests.cs` (98 tests total, covering all security scenarios)

---

### Issue Tracker Tools

#### `list_issues`
- **Class**: `ListIssuesTool` (`/Tools/Connector/ListIssuesTool.cs`)
- **Description**: Lists open issues or feature requests from the configured issue tracker (e.g., GitHub).
- **Parameters**: `state` (optional), `labels` (optional array)

#### `get_issue`
- **Class**: `GetIssueTool` (`/Tools/Connector/GetIssueTool.cs`)
- **Description**: Retrieves the details of a specific issue by ID.
- **Parameters**: `issueId`

#### `create_issue`
- **Class**: `CreateIssueTool` (`/Tools/Connector/CreateIssueTool.cs`)
- **Description**: Creates a new issue, feature request, or bug report. Maps `type` and `size` to issue tracker labels automatically.
- **Parameters**: `title`, `body`, `type`, `size` (optional)

#### `add_issue_comment`
- **Class**: `AddIssueCommentTool` (`/Tools/Connector/AddIssueCommentTool.cs`)
- **Description**: Adds a comment to an existing issue (e.g., to link a completed task or commit).
- **Parameters**: `issueId`, `body`

---

### Wiki Tools

#### `get_wiki_page`
- **Class**: `GetWikiPageTool` (`/Tools/Connector/GetWikiPageTool.cs`)
- **Description**: Retrieves the contents of a wiki page (from local filesystem or external XpectoLive).
- **Parameters**: `pathOrId` (relative path or Page ID)

#### `create_wiki_page`
- **Class**: `CreateWikiPageTool` (`/Tools/Connector/CreateWikiPageTool.cs`)
- **Description**: Creates a new wiki page.
- **Parameters**: `title`, `content`, `parentPathOrId` (optional)

#### `update_wiki_page`
- **Class**: `UpdateWikiPageTool` (`/Tools/Connector/UpdateWikiPageTool.cs`)
- **Description**: Updates the contents of an existing wiki page.
- **Parameters**: `pathOrId`, `content`

#### `move_wiki_page`
- **Class**: `MoveWikiPageTool` (`/Tools/Connector/MoveWikiPageTool.cs`)
- **Description**: Moves (and optionally renames) an existing wiki page to a new parent location.
- **Parameters**:
  - `pathOrId` (string, **required**) – For filesystem wiki: relative markdown file path of the page to move. For XpectoLive wiki: the Page ID.
  - `newPathOrParentId` (string, **required**) – For filesystem wiki: relative path of the target parent directory. For XpectoLive wiki: the target parent Page ID. Use an empty string to move to the root.
  - `newTitle` (string, optional) – New title for the page. For filesystem wiki, this also determines the new filename slug.
- **Behavior by connector**:
  - **FileSystemWikiConnector**: Uses `File.Move` to relocate the `.md` file; creates the target directory if it does not exist; optionally slugifies `newTitle` as the new filename.
  - **GitHubWikiConnector**: Same as filesystem, then commits and pushes the change: `"Move wiki page: {source} -> {dest}"`.
  - **XpectoLiveWikiConnector**: Delegates to `_client.MovePageAsync` with `TargetSpaceId = _spaceId`; if `newTitle` is provided, additionally calls `UpdatePageDraftAsync` + `PublishPageDraftAsync` to rename the page.

#### `search_wiki`
- **Class**: `SearchWikiTool` (`/Tools/Connector/SearchWikiTool.cs`)
- **Description**: Searches for content or titles in the configured wiki.
- **Parameters**: `query`
