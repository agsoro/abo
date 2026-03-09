# 🤖 Agsoro Bot Orchestrator (ABO)

**ABO** is a lightweight, privacy-first AI agent orchestration framework built in **C# / .NET 10**. It serves as the intelligent layer atop internally developed ticket and work-tracking systems, transforming static data into an active, automated product management ecosystem.

The framework is designed for organizations that prioritize **data sovereignty**. It uses a "Native Orchestrator" pattern, communicating with AI models via standard REST/API calls to any compatible endpoint—whether hosted on-premises or via a secure private gateway.

---

## 🚀 Key Features

* **Privacy-Centric Design:** ABO is built to run entirely within your secure network. It requires only a REST API endpoint; no proprietary SDKs or "phone-home" telemetry are included.
* **Pure .NET 10 Implementation:** Built using standard `HttpClient` and `System.Text.Json` for maximum performance, transparency, and ease of maintenance for C# developers.
* **Agnostic Backend:** Seamlessly switch between local inference servers, private cloud instances, or managed gateways by simply updating a Base URL.
* **Intelligent Agent Selection:** Automatically routes user requests to the best available specialized agent (e.g., Quiz, Greetings, Time-keeping).

---

## 🏗️ Architecture: The "Agent Loop"

ABO operates on a **Controller-Worker** loop, ensuring the AI never has direct, unmonitored access to your data:

1.  **Request:** The Orchestrator (C#) receives a ticket or user query.
2.  **Reasoning:** A `POST` request is sent to the configured AI Endpoint. The model analyzes the intent and returns a "Tool Call" (JSON).
3.  **Local Execution:** The C# Orchestrator parses the JSON and executes the corresponding internal method (e.g., `QueryTicketDB` or `UpdateStatus`).
4.  **Synthesis:** The results are sent back to the API for a final, human-readable summary or action confirmation.

---

## 🛠️ Configuration

Configure your environment in `appsettings.json`. ABO treats the AI backend as a simple service endpoint.

```json
{
  "config": {
    "ApiEndpoint": "https://your-internal-ai-gateway/v1",
    "ModelName": "custom-pm-model-v1",
    "TimeoutSeconds": 30
  }
}
```

## 📂 Project Structure
* /agents: Definitions for specialized roles (e.g., `QuizAgent`, `HelloWorldAgent`).
* /tools: C# plugins that the AI can trigger (e.g., `QuizTools`, `GetSystemTimeTool`).
* /core: The core loop logic, REST client, and `AgentSupervisor` implementation.
* /contracts: JSON schemas and DTOs used for API interaction.
