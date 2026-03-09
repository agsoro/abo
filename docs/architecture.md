# ABO Architecture

The Agsoro Bot Orchestrator (ABO) is built around a **Controller-Worker** loop designed for maximum privacy and data sovereignty. It implements a "Native Orchestrator" pattern.

## The Agent Loop

The core orchestration loop ensures that the AI never has direct, unmonitored access to your internal data. All actions are brokered through the C# orchestrator:

1. **Request**: The loop begins when the Orchestrator receives a user query or a system event (e.g., a new ticket).
2. **Reasoning**: The Orchestrator constructs a prompt and tools schema (using the standard OpenAI tools format), sending a pure REST `POST` request to the configured AI Endpoint (e.g., OpenRouter). The model analyzes the intent and determines the necessary actions, returning a "Tool Call" structured in JSON.
3. **Local Execution**: The C# Orchestrator parses the JSON tool call, validates it against predefined contracts, and securely executes the corresponding internal C# method (such as `QueryTicketDB` or `UpdateStatus`).
4. **Synthesis**: The result of the local execution is sent back to the AI model to generate a final, human-readable summary, or to confirm the action.

## Looser Architecture (vs. MCP)

ABO deliberately avoids full Model Context Protocol (MCP) server/client overhead in favor of a simpler, looser architecture. 
*   Instead of managing external MCP servers, tools are defined directly in C#.
*   Instead of complex transports, ABO serializes its internal capabilities using the widely-supported "tools" API array standard (compatible with OpenRouter, OpenAI, and most major models).
*   This ensures the orchestrator remains incredibly lightweight, requiring only basic JSON serialization to expose private C# methods to the AI.

## Privacy First
Because all tool executions happen locally in C#, sensitive systems are never exposed directly to the AI service. The AI only sees the specific data provided during the Reasoning and Synthesis phases.
