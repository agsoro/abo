# ABO Project

## Overview

Welcome to the ABO Project. This document serves as the central entry point into the project documentation and summarizes the new distributed architecture. The project consists of multiple loosely coupled services and libraries focused on Project Management and Workflow automation.

## Current Directory Structure

The solution has been refactored into the following core projects:

- **`/Abo.Pm/`**: The main Product Management backend. It hosts the PM-specific API endpoints (e.g., `/api/projects`, `/api/open-work`), the application agents (`PmoAgent`, `ManagerAgent`, `SpecialistAgent`), and their integration tools.
  - **`/Abo.Pm/Docs/`**: Contains architectural and conceptual documentation (agents, tools, configuration).
- **`/Abo.Workflow/`**: A standalone Web API dedicated to BPMN process definitions. It hosts the `/api/processes` endpoints and the visual BPMN frontend (`/processes/index.html`).
- **`/Abo.Core/`**: Contains shared contracts, abstractions (e.g., `IAboTool`), and core integration logic like Issue Tracker Connectors.
- **`/Abo.Quiz/`**: Dedicated module encapsulating the QuizAgent and specific quiz functionalities.
- **`/Abo.Tests/`**: Unit and integration tests covering the projects above.

## Quickstart / Getting Started

1. **Clone the repository**: Make sure all relevant files and directories are present.
2. **Review Configuration**: Configure the environment settings (e.g., `environments.json` in the runtime `Data` folder) and necessary secrets (like `Integrations:GitHub:Token` and `Integrations:XpectoLive`).
3. **Run the Applications**:
   - `dotnet run --project Abo.Pm` to start the main Product Management API and agents.
   - `dotnet run --project Abo.Workflow` to start the standalone BPMN Web API for workflow definitions.
4. **Testing**: Run `dotnet test Abo.sln` from the root directory to verify system integrity.

## Documentation Notes

- **Documentation updates:** The README and project structure have been recently modernized to separate Project Management logic and dynamic BPMN logic.
- **Data structure:** Runtime project data is typically generated inside the `bin/Debug/net9.0/Data` directories of the respective executing services (`Abo.Pm` or `Abo.Workflow`).
