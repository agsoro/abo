# Tools and Plugins

Tools in ABO are local C# methods that act as plugins, which the AI model can trigger via JSON-based tool calls. They are located in the `/tools` directory.

## Implementation Details

Since ABO uses pure .NET 10 without heavy proprietary SDKs, tools are standard C# classes. 

### Security, Execution, and the "Tools" Array
When the AI model requests a tool execution:
1. The Orchestrator serializes available tools into the standard JSON `tools` array format expected by OpenRouter (and most major models).
2. The model returns a tool call payload. The Orchestrator validates the requested tool against the JSON schemas defined in `/contracts`.
3. The parameters are safely deserialized using `System.Text.Json`.
4. The corresponding C# method (e.g., `GetBacklogHistory`) is invoked securely within the local environment.
5. The output is serialized and returned to the model as a tool message.

This intentionally avoids the complexity of full MCP (Model Context Protocol). By using the simpler, looser architecture of directly passing JSON schemas to OpenRouter, ABO guarantees that the AI has zero direct access to your databases or internal APIs; it only has access to the curated set of C# tools you explicitly provide via the `tools` array.
