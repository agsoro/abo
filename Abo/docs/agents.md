# Intelligent Agent Selection

ABO supports a specialized multi-agent architecture. Instead of a single monolithic bot, ABO uses specialized agents coordinated by a **Supervisor**.

## Agent Supervisor

The `AgentSupervisor` is the entry point for all user interactions. It uses the LLM to analyze the user's intent and select the best agent based on their `Name` and `Description`.

## Registered Agents

Agents in ABO are defined as specialized roles with specific instructions, tools, and constraints.

### HelloWorldAgent
- **Description**: A basic assistant for general greetings and testing.
- **Tools**: Can tell the system time in German and ask about personal preferences (multiple choice).

### QuizAgent
- **Description**: A specialized agent for tech and nerdy trivia.
- **Tools**: Handles quiz subscriptions, leaderboards, and asking multiple-choice trivia questions.
- **Rules**: Follows strict scoring (points only for correct answers) and mandatory web references for all feedback.

## Implementation
Agents implement the `IAgent` interface and are registered as transient services in `Program.cs`.
