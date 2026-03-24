# ABO – Agsoro Bot Orchestrator

## Overview

ABO (Agsoro Bot Orchestrator) is a lightweight, privacy-first AI orchestration platform for automating issue management and project workflows. It uses a multi-agent architecture where a Supervisor routes requests to specialized agents that perform work via local tool calls — no data ever leaves your environment unless you choose to send it.

The solution consists of multiple loosely coupled services and libraries focused on Issue Management and Workflow automation.

## 📖 Documentation

Full documentation is available on the **[GitHub Wiki](https://github.com/agsoro/abo/wiki)**:

- [Architecture](https://github.com/agsoro/abo/wiki/Architecture/overview) — Agent loop, connectors, runtime data, web API
- [Agents](https://github.com/agsoro/abo/wiki/Agents/overview) — ManagerAgent, SpecialistAgent, roles
- [Tools](https://github.com/agsoro/abo/wiki/Tools/overview) — All tools and security model
- [Configuration](https://github.com/agsoro/abo/wiki/Configuration/appsettings) — appsettings, secrets, environments
- [Workflows](https://github.com/agsoro/abo/wiki/Workflows/overview) — BPMN workflow phases and transitions

## Current Directory Structure

The solution is organized into the following core projects:

- **`/Abo.Pm/`**: The main Product Management backend. Hosts the PM-specific API endpoints (e.g., `/api/issues`, `/api/open-work`), the application agents (`ManagerAgent`, `SpecialistAgent`), and their integration tools.
  - **`/Abo.Pm/Docs/`**: Contains data artifacts (`wiki_schemas.json`, `xpectolive-swagger.json`). Documentation has moved to the [wiki](https://github.com/agsoro/abo/wiki).
- **`/Abo.Core/`**: Contains shared contracts, abstractions (e.g., `IAboTool`), and core integration logic like Issue Tracker and Wiki Connectors.
- **`/Abo.Tests/`**: Unit and integration tests covering the projects above.
- **`/Ideas/`**: Experimental and parked ideas (e.g., `Abo.Quiz`, `Abo.Workflow`).

## Quickstart / Getting Started

1. **Clone the repository**: Make sure all relevant files and directories are present.
2. **Review Configuration**: Configure the environment settings (`environments.json` in the runtime `Data` folder) and necessary secrets (see [Configuration – Secrets](https://github.com/agsoro/abo/wiki/Configuration/secrets)).
3. **Run the Application**:
   - `dotnet run --project Abo.Pm` to start the main Product Management API and agents.
4. **Testing**: Run `dotnet test Abo.sln` from the root directory to verify system integrity.
5. **Access the Web UI** at `http://localhost:{port}/` or connect via **Mattermost**.
