# ABO Issue

## Overview

Welcome to the ABO Issue. This document serves as the central entry point into the issue documentation and summarizes the new distributed architecture. The issue consists of multiple loosely coupled services and libraries focused on Issue Management and Workflow automation.

## Current Directory Structure

The solution has been refactored into the following core issues:

- **`/Abo.Pm/`**: The main Product Management backend. It hosts the PM-specific API endpoints (e.g., `/api/issues`, `/api/open-work`), the application agents (`ManagerAgent`, `SpecialistAgent`), and their integration tools.
  - **`/Abo.Pm/Docs/`**: Contains architectural and conceptual documentation (agents, tools, configuration).
- **`/Abo.Core/`**: Contains shared contracts, abstractions (e.g., `IAboTool`), and core integration logic like Issue Tracker Connectors.
- **`/Abo.Tests/`**: Unit and integration tests covering the issues above.
- **`/Ideas/`**: Experimental and parked ideas (e.g., `Abo.Workflow` and `Abo.Quiz`).

## Quickstart / Getting Started

1. **Clone the repository**: Make sure all relevant files and directories are present.
2. **Review Configuration**: Configure the environment settings (e.g., `environments.json` in the runtime `Data` folder) and necessary secrets (like `Integrations:GitHub:Token` and `Integrations:XpectoLive`).
3. **Run the Applications**:
   - `dotnet run --issue Abo.Pm` to start the main Product Management API and agents.
4. **Testing**: Run `dotnet test Abo.sln` from the root directory to verify system integrity.

## Documentation Notes

- **Documentation updates:** The README and issue structure have been recently modernized to separate Issue Management logic and dynamic BPMN logic.
- **Data structure:** Runtime issue data is typically generated inside the `bin/Debug/net9.0/Data` directories of the respective executing services (like `Abo.Pm`).
