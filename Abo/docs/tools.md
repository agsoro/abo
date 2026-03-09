# Tools and Plugins

Tools in ABO are local C# methods that act as plugins, which the AI model can trigger via JSON-based tool calls. They are located in the `/Tools` directory.

## Implementation Details

Since ABO uses pure .NET 10 without heavy proprietary SDKs, tools are standard C# classes implementing the `IAboTool` interface.

### Security, Execution, and the "Tools" Array
When the AI model requests a tool execution:
1. The Orchestrator (or a specialized Agent) serializes available tools into the standard JSON `tools` array format.
2. The model returns a tool call payload.
3. The parameters are safely deserialized using `System.Text.Json`.
4. The corresponding C# method (e.g., `AskMultipleChoiceTool`) is invoked securely within the local environment.
5. The output is serialized and returned to the model as a tool message.

This intentionally avoids the complexity of full MCP (Model Context Protocol). By using the simpler architecture of directly passing JSON schemas to the LLM, ABO guarantees that the AI has zero direct access to your databases or internal APIs; it only has access to the curated set of C# tools you explicitly provide.
