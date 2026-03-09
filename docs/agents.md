# Multi-Agent Triage

ABO supports a specialized multi-agent architecture to handle complex workflows such as ticket categorization, roadmap grouping, and automated PRD drafting.

## Agent Definitions

Agents in ABO are defined as specialized roles with specific instructions, tools, and constraints. They reside in the `/agents` directory.

### Example Roles
- **Triage Agent**: Specializes in categorizing incoming tickets, determining priority, and routing them to the appropriate team.
- **Roadmap Analyst**: Groups related tickets and requests to assist in roadmap planning and feature prioritization.
- **PRD Drafter**: Automatically synthesizes product requirements documents from multiple related work items.

## Agent Collaboration
Agents can hand off tasks or synthesize results sequentially, coordinated by the main Orchestrator loop.
