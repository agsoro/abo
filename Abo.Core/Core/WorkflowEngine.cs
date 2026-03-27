using System;
using System.Collections.Generic;
using Abo.Contracts.Models;

namespace Abo.Core;

public static class WorkflowEngine
{
    public static string ResolveStepIdFallback(IssueRecord issue)
    {
        var stepId = issue.StepId ?? string.Empty;

        // If the step is recognized natively, accept it immediately
        if (!string.IsNullOrWhiteSpace(stepId) && GetStepInfo(stepId) != null) return stepId;

        // If the step is blank OR unrecognized (e.g. 'Open', 'Todo') but within the 'requested' project, force it
        if (string.Equals(issue.Project, "requested", StringComparison.OrdinalIgnoreCase)) return "open";

        return stepId;
    }

    public static ProcessStepInfo? GetStepInfo(string stepId)
    {
        return stepId.ToLower() switch
        {
            "open" => new ProcessStepInfo 
            { 
                StepId = "open", 
                StepName = "Triage Request", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_Productmanager",
                    Title = "Product Manager",
                    SystemPrompt = @"You are the Product Manager. Your primary goal is to oversee features, check triage requests and plan if and when a issue should be worked on. You engage with the issue tracker actively. DO NOT write code or modify files directly.
                    ### TRIAGE RULES FOR update_issue (MANDATORY):
                    When you rephrase or standardize an issue's title or body using `update_issue`, you MUST preserve the reporter's original text. Follow these steps:
                    1. Before calling `update_issue`, fetch the current issue via `get_issue` to capture the original title and body.
                    2. Write the new, standardized body (concise, technical, actionable).
                    3. Include the original title and body in the new body, but clearly mark them as original.
                    ### TRIAGE DECISION:
                    At triage, your decision is BINARY:
                    - **Valid issue**: route to `release-planning` using keyword 'Triage OK'. Do NOT assign `release-current` or `release-next` — that is the Release Planner's job.
                    - **Invalid/Duplicate**: route to `invalid` using keyword 'Reject or Duplicate'.",
                    AllowedTools = new List<string> { "list_issues", "get_issue", "add_issue_comment", "update_issue", "get_wiki_page", "read_file", "list_dir", "search_wiki" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Reject or Duplicate", NextStepId = "invalid", ApplyState = issue => ApplyTransitionAndState(issue, "requested", "closed") },
                    new WorkflowTransition { ConditionName = "Triage OK", NextStepId = "release-planning", ApplyState = issue => ApplyTransitionAndState(issue, "planned", "open") }
                }
            },
            "release-planning" => new ProcessStepInfo 
            { 
                StepId = "release-planning", 
                StepName = "Release Planning", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_Releaseplanner",
                    Title = "Release Planner",
                    SystemPrompt = @"You are the Release Planner. Your responsibility is to prioritize issues from the planning backlog and assign them to the correct release bucket.
                    ### YOUR TASK:
                    You are given a single issue at the `release-planning` step. Your job is to decide whether this issue belongs in:
                    - `release-current` — work should be done in the current release sprint
                    - `release-next` — work should be scheduled to a later release
                    ### HOW TO DECIDE:
                    1. Read the issue carefully using `get_issue`.
                    2. Use `list_issues` to check the current size and state of `release-current`. If it is large (>5 open issues), prefer assigning to `release-next` or backlog unless the issue is critical.
                    3. Consult `get_wiki_page` or `search_wiki` for any release planning guidelines or documentation.
                    4. Use `add_issue_comment` to document your rationale before completing the task.
                    5. When ready, call `complete_task` with the appropriate keyword:
                       - `'Assign to current release'` → places in `release-current`
                       - `'Assign to next release'` → places in `release-next`
                    ### RULES:
                    - DO NOT write code or modify source files.
                    - Keep `release-current` focused: prefer quality over quantity.",
                    AllowedTools = new List<string> { "list_issues", "get_issue", "update_issue", "add_issue_comment", "search_wiki", "get_wiki_page" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Assign to current release", NextStepId = "planned", ApplyState = issue => ApplyTransitionAndState(issue, "release-current", "open") },
                    new WorkflowTransition { ConditionName = "Assign to next release", NextStepId = "planned", ApplyState = issue => ApplyTransitionAndState(issue, "release-next", "open") },
                    new WorkflowTransition { ConditionName = "Defer to backlog", NextStepId = "planned", ApplyState = issue => ApplyTransitionAndState(issue, "planned", "open") }
                }
            },
            "planned" => new ProcessStepInfo 
            { 
                StepId = "planned", 
                StepName = "Solution Planning", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_Architect",
                    Title = "Software Architect",
                    SystemPrompt = @"You are the Software Architect. You receive triaged requests and plan technical solutions. Your responsibilities include:
                    Claiming Tickets: Take ownership of architectural tasks.
                    Outlining Changes: Provide a high-level roadmap of required modifications.
                    Ignore routine implementation details or boilerplate.
                    Defining the Technical Approach: Establish the fundamental strategy and patterns before passing the work to Developers for execution.
                    High-Impact Wiki Documentation: Document only the major architectural and technical pillars that affect the project's long-term integrity. 
                    ### TASK COMPLETION:
                    Call `complete_task` with one of these keywords:
                    - 'Solution planned' -> Moves to implementation (work).
                    - 'Problem: Need more input/help' -> Moves to waiting customer.",
                    AllowedTools = new List<string> { "read_file", "list_dir", "search_regex", "get_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki", "create_sub_issue" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Problem: Need more input/help", NextStepId = "waiting customer", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") },
                    new WorkflowTransition { ConditionName = "Solution planned", NextStepId = "work", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") }
                }
            },
            "work" => new ProcessStepInfo 
            { 
                StepId = "work", 
                StepName = "Implementation", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_Developer",
                    Title = "Developer",
                    SystemPrompt = @"You are a Software Developer. Your role is to transform architectural plans into high-quality code. You implement solutions, create files, compile, test, and perform technical refactorings. You do not push to production or release code.
                    ### OPERATIONAL GUIDELINES:
                    * **Plan Adherence:** Strictly follow the technical approach defined by the Architect. Do not deviate from the macro-level structure or documented 'Big Picture' patterns.
                    * **Code Integrity:** Ensure all code is modular, tested, and follows the project's established styling.
                    * **Scope Control:** Focus on the implementation of the specific ticket; move peripheral architectural concerns back to the Architect.

                    ### GIT BRANCHING WORKFLOW (MANDATORY):
                    Before making ANY code changes, you MUST create and switch to a dedicated feature branch:
                    1. **Sync:** git checkout main && git pull origin main
                    2. **Branch:** git checkout -b feature/issue-{issueId}-{short-description}
                    (Replace {issueId} with the numeric ID and {short-description} with a kebab-case summary, e.g., 'feature/issue-85-git-workflow')
                    3. **Develop:** Make all changes, commits, and pushes on this branch — NEVER commit directly to main.
                    4. **Handoff:** git push origin feature/issue-{issueId}-{short-description}
                    5. **Report:** Include the exact branch name in your resultNotes so the Release Engineer knows which branch to merge.",
                    AllowedTools = new List<string> { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "shell", "search_regex", "http_get", "get_issue", "add_issue_comment", "get_wiki_page", "update_wiki_page", "search_wiki" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Problem: Need more input/help", NextStepId = "waiting customer", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") },
                    new WorkflowTransition { ConditionName = "Implementation completed", NextStepId = "review", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") }
                }
            },
            "review" => new ProcessStepInfo 
            { 
                StepId = "review", 
                StepName = "QA Review", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_QA",
                    Title = "QA Engineer",
                    SystemPrompt = @"You are a QA Engineer. Your mission is to validate that the solution fulfills original requirements and adheres to the Architect's plan. 
                    ### RESPONSIBILITIES:
                    * **Validation:** Review developer code, run automated tests, and invoke builds.
                    * **Documentation Audit:** Update the wiki with final technical notes. Verify that the Architect's 'High-Impact' documentation (ADRs, Topology) accurately reflects the final state of the implementation.
                    * **Non-Invasive Role:** You can run system commands and review code, but you DO NOT write code or modify files directly. 
                    * **Gatekeeping:** If any issue is found or if documentation is missing/inaccurate, document the findings and REJECT the flow. You do not push/release the code.",
                    AllowedTools = new List<string> { "read_file", "list_dir", "git", "dotnet", "python", "shell", "search_regex", "http_get", "get_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Problem: Solution rejected", NextStepId = "planned", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") },
                    new WorkflowTransition { ConditionName = "Solution accepted", NextStepId = "check", ApplyState = issue => ApplyTransitionAndState(issue, null, "open") }
                }
            },
            "check" => new ProcessStepInfo 
            { 
                StepId = "check", 
                StepName = "Release", 
                Role = new RoleDefinition
                {
                    RoleId = "Role_Releaseengineer",
                    Title = "Release Engineer",
                    SystemPrompt = @"You are a Release Engineer. Your sole focus is the secure integration of code into the main branch. You DO NOT change documentation or code.
                    ### GIT MERGE WORKFLOW (MANDATORY):
                    1. **Locate Branch:** Read issue comments/resultNotes to find the feature branch (format: feature/issue-{id}-{description}).
                    2. **Sync:** git checkout main && git pull origin main
                    3. **Merge:** git merge --no-ff feature/issue-{id}-{description}
                    4. **Deploy:** git push origin main
                    5. **Cleanup:** - git branch -d feature/issue-{id}-{description}
                    - git push origin --delete feature/issue-{id}-{description}

                    ### GUARDRAILS:
                    * **Conflict Resolution:** If a merge conflict occurs, use 'request_ceo_help' to escalate — do NOT attempt to resolve conflicts autonomously.
                    * **Scope:** You only release code that has been cleared by QA.",
                    AllowedTools = new List<string> { "read_file", "list_dir", "git", "get_issue", "add_issue_comment" }
                },
                Transitions = new List<WorkflowTransition>
                {
                    new WorkflowTransition { ConditionName = "Release finished", NextStepId = "done", ApplyState = issue => ApplyTransitionAndState(issue, null, "closed") }
                }
            },
            "done" => new ProcessStepInfo { StepId = "done", StepName = "Completed" },
            "invalid" => new ProcessStepInfo { StepId = "invalid", StepName = "Rejected or Duplicate" },
            "waiting customer" => new ProcessStepInfo { StepId = "waiting customer", StepName = "Waiting for Customer Input" },
            _ => null
        };
    }

    public static List<WorkflowTransition> GetTransitions(string stepId)
    {
        return GetStepInfo(stepId)?.Transitions ?? new List<WorkflowTransition>();
    }

    private static void ApplyTransitionAndState(IssueRecord issue, string? project, string state = "open")
    {
        if (project != null) issue.Project = project;
        issue.State = state;
    }
}
