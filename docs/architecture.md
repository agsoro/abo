# ABO Architecture

The Agsoro Bot Orchestrator (ABO) is built around a **Controller-Worker** loop designed for maximum privacy and data sovereignty. It implements a "Native Orchestrator" pattern.

## The Agent Loop

The core orchestration loop ensures that the AI never has direct, unmonitored access to your internal data. All actions are brokered through the C# orchestrator:

1. **Intelligent Selection**: When a request arrives, the `AgentSupervisor` uses the LLM to analyze the intent and select the most appropriate specialized agent (e.g., `QuizAgent` or `HelloWorldAgent`).
2. **Reasoning**: The selected agent provides its `SystemPrompt` and `ToolDefinitions`. The Orchestrator sending a REST `POST` request to the AI Endpoint. The model returns a "Tool Call" in JSON.
3. **Local Execution**: The C# Orchestrator parses the JSON tool call and executes the corresponding internal C# tool method.
4. **Synthesis**: The result is sent back to the AI model to generate a final, human-readable summary.

## Looser Architecture (vs. MCP)

ABO deliberately avoids full Model Context Protocol (MCP) server/client overhead in favor of a simpler, looser architecture. 
*   Instead of managing external MCP servers, tools are defined directly in C#.
*   Instead of complex transports, ABO serializes its internal capabilities using the widely-supported "tools" API array standard (compatible with OpenRouter, OpenAI, and most major models).
*   This ensures the orchestrator remains incredibly lightweight, requiring only basic JSON serialization to expose private C# methods to the AI.

## Privacy First
Because all tool executions happen locally in C#, sensitive systems are never exposed directly to the AI service. The AI only sees the specific data provided during the Reasoning and Synthesis phases.

## External Integrations Pattern

When ABO needs to interact with external services (like XpectoLive or Mattermost), we strongly separate **HTTP Infrastructure** from **AI Tool execution**.

*   **/Integrations/{Platform}**: Contains strongly-typed HTTP Clients (`XpectoLiveClient`) that handle authentication, retries, serialization, and raw API routing. These classes know nothing about AI or tool calling schemas.
*   **/Tools/{Domain}**: Contains lightweight C# plugins implementing `IAboTool`. These tools accept the LLM's arguments in JSON, request the required client (e.g., `XpectoLiveClient`) via Dependency Injection, and map the infrastructure responses into simple text for the LLM. 

This prevents the LLM tools from becoming bloated with authentication and HTTP retry logic.
