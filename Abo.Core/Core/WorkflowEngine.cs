using System;
using System.Collections.Generic;
using Abo.Contracts.Models;

namespace Abo.Core;

public static class WorkflowEngine
{
    public static class StepId
    {
        public const string Open = StatusType.Open;
        public const string PlanningBacklog = StatusType.Planned + "_" + ProjectType.Backlog;
        public const string PlanningReleaseCurrent = StatusType.Planned + "_" + ProjectType.ReleaseCurrent;
        public const string PlanningReleaseCurrentDoc = StatusType.Planned + "_" + ProjectType.ReleaseCurrent + "_" + IssueType.Doc;
        public const string PlanningReleaseNext = StatusType.Planned + "_" + ProjectType.ReleaseNext;
        public const string WorkReleaseCurrent = StatusType.Work + "_" + ProjectType.ReleaseCurrent;
        public const string WorkReleaseCurrentDoc = StatusType.Work + "_" + ProjectType.ReleaseCurrent + "_" + IssueType.Doc;
        public const string ReviewReleaseCurrent = StatusType.Review + "_" + ProjectType.ReleaseCurrent;
        public const string ReviewReleaseCurrentDoc = StatusType.Review + "_" + ProjectType.ReleaseCurrent + "_" + IssueType.Doc;
        public const string CheckReleaseCurrent = StatusType.Check + "_" + ProjectType.ReleaseCurrent;
        public const string ReviewReleaseNext = StatusType.Review + "_" + ProjectType.ReleaseNext;
        public const string CheckReleaseNext = StatusType.Check + "_" + ProjectType.ReleaseNext;
        public const string Invalid = StatusType.Invalid;
        public const string WaitingCustomer = StatusType.WaitingCustomer;
        public const string Done = StatusType.Done;

        public static readonly IReadOnlyList<string> AllowedValues = new[]
        {
            Open, PlanningBacklog, PlanningReleaseCurrent, PlanningReleaseNext, PlanningReleaseCurrentDoc, WorkReleaseCurrent, WorkReleaseCurrentDoc, ReviewReleaseCurrent, ReviewReleaseCurrentDoc, CheckReleaseCurrent, ReviewReleaseNext, CheckReleaseNext, Invalid, WaitingCustomer, Done
        };

        public static string ToStepId(IssueRecord issue)
        {
            if (string.IsNullOrWhiteSpace(issue.Status) || string.IsNullOrWhiteSpace(issue.Project))
                return Invalid;

            if (AllowedValues.Contains(issue.Status + "_" + issue.Project + "_" + issue.Type, StringComparer.OrdinalIgnoreCase))
                return issue.Status + "_" + issue.Project + "_" + issue.Type;

            if (AllowedValues.Contains(issue.Status + "_" + issue.Project, StringComparer.OrdinalIgnoreCase))
                return issue.Status + "_" + issue.Project;

            if (AllowedValues.Contains(issue.Status, StringComparer.OrdinalIgnoreCase))
                return issue.Status;

            return Invalid;
        }
    }

    public static string ResolveStatusFallback(IssueRecord issue)
    {
        var status = issue.Status ?? string.Empty;

        // If the status is valid, accept it immediately
        if (StatusType.IsValid(status)) return status;

        return StatusType.Open;
    }

    public static string ResolveProjectFallback(IssueRecord issue)
    {
        var project = issue.Project ?? string.Empty;

        // If the project is valid, accept it immediately
        if (ProjectType.IsValid(project)) return project;

        return ProjectType.Requested;
    }

    public static ProcessStepInfo? GetStepInfo(IssueRecord issue)
    {
        var status = ResolveStatusFallback(issue).ToLower();
        var project = ResolveProjectFallback(issue).ToLower();

        var stepId = StepId.ToStepId(new IssueRecord { Status = status, Project = project, Type = issue.Type });

        return stepId switch
        {
            StepId.Open => new ProcessStepInfo
            {
                StepId = StepId.Open,
                StepName = "Triage Request",
                Role = new RoleDefinition
                {
                    RoleId = "Role_Productmanager",
                    Title = "Product Manager",
                    SystemPrompt = @"You are the Product Manager. Your primary goal is to oversee features, check triage requests, 
                    and plan if and when an issue should be worked on. You actively engage with the issue tracker.

                    ### RULES & GUIDELINES
                    * **Non-Invasive:** DO NOT write code or modify files directly.
                    * **Preserve Context:** When rephrasing or standardizing an issue's title or body, you MUST preserve the reporter's original text.

                    ### WORKFLOW (MANDATORY)
                    1. Fetch the current issue via `get_issue` to capture the original title and body.
                    2. Write the new, standardized body (concise, technical, actionable).
                    3. Include the original title and body in the new body, clearly marked as original.
                    4. Update the issue using `update_issue`.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include any standard context in the mandatory `notes` parameter. 
                    - 'triage_ok' -> Moves issue to release planning.
                    - 'reject_duplicate' -> Marks issue as invalid and ends the flow.",
                    AllowedTools = new List<string> { "conclude_step", "list_issues", "get_issue", "update_issue", "get_wiki_page", "read_file", "list_dir", "search_wiki" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "reject_duplicate", new WorkflowTransition { NextStepId = StepId.Invalid, IsEndEvent = true, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Invalid, ProjectType.Requested, StateType.Closed) } },
                    { "triage_ok", new WorkflowTransition { NextStepId = StepId.PlanningBacklog, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Planned, ProjectType.Backlog, StateType.Open) } }
                }
            },
            StepId.PlanningBacklog => new ProcessStepInfo
            {
                StepId = StepId.PlanningBacklog,
                StepName = "Release Planning",
                Role = new RoleDefinition
                {
                    RoleId = "Role_Releaseplanner",
                    Title = "Release Planner",
                    SystemPrompt = @"You are the Release Planner. Your responsibility is to prioritize issues from the planning backlog 
                    and assign them to the correct release bucket.

                    ### RULES & GUIDELINES
                    * **Non-Invasive:** DO NOT write code or modify source files.
                    * **Focus on Quality:** Keep `release-current` focused. Prefer quality over quantity. If `release-current` is 
                    large (>5 open issues), prefer assigning to `release-next` or backlog unless the issue is critical.

                    ### WORKFLOW (MANDATORY)
                    1. Read the issue carefully using `get_issue`.
                    2. Use `list_issues` to check the current size and state of `release-current`.
                    3. Consult `get_wiki_page` or `search_wiki` for any release planning guidelines.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Document your rationale in the mandatory `notes` parameter. 
                    - 'assign_current' -> Assigns work to the current release sprint.
                    - 'assign_next' -> Schedules work for a later release.
                    - 'reject_duplicate' -> Marks issue as invalid and ends the flow.",
                    AllowedTools = new List<string> { "conclude_step", "list_issues", "get_issue", "update_issue", "search_wiki", "get_wiki_page" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "assign_current", new WorkflowTransition { NextStepId = StepId.PlanningReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Planned, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "assign_next", new WorkflowTransition { NextStepId = StepId.PlanningReleaseNext, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Planned, ProjectType.ReleaseNext, StateType.Open) } },
                    { "reject_duplicate", new WorkflowTransition { NextStepId = StepId.Invalid, IsEndEvent = true, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Invalid, ProjectType.Requested, StateType.Closed) } }
                }
            },
            StepId.PlanningReleaseCurrentDoc => new ProcessStepInfo
            {
                StepId = StepId.PlanningReleaseCurrentDoc,
                StepName = "Documentation Planning",
                Role = new RoleDefinition
                {
                    RoleId = "Role_InformationArchitect",
                    Title = "Information Architect",
                    SystemPrompt = @"You are the Information Architect. You receive triaged documentation requests and plan the macro-level 
                    structure of the knowledge base or wiki.

                    ### RULES & GUIDELINES
                    * **High-Level Planning:** Provide a high-level outline of the documentation that needs to be written or updated. Define the target audience and tone.
                    * **Delegation:** Do NOT write the step-by-step content or final markdown yourself; leave execution to the Technical Writer.
                    * **Task Breakdown:** If the documentation request is massive, use `create_sub_issue` to break the work down into smaller, manageable tickets for Technical Writers.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Provide your outline and guidance in the mandatory `notes` parameter.
                    - 'solution_planned' -> Outline is ready and/or sub-issues created, moves to execution.
                    - 'pause_work' -> Pauses the workflow for this issue (to be resumed later).
                    - 'need_help' -> Escalates to the customer for clarification.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "list_dir", "get_issue", "get_wiki_page", "search_wiki", "create_sub_issue" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "need_help", new WorkflowTransition { NextStepId = StepId.WaitingCustomer, IsEndEvent = true, ApplyState = i => ApplyTransitionAndState(i, StatusType.WaitingCustomer, ProjectType.ReleaseCurrent, StateType.Open) } },
                        { "pause_work", new WorkflowTransition { NextStepId = StepId.PlanningReleaseCurrentDoc, ApplyState = i => ApplyTransitionAndState(i, StatusType.Planned, ProjectType.ReleaseCurrent, StateType.Open) } },
                        { "solution_planned", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrentDoc, ApplyState = i => ApplyTransitionAndState(i, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } }
                    }
            },
            StepId.PlanningReleaseCurrent => new ProcessStepInfo
            {
                StepId = StepId.PlanningReleaseCurrent,
                StepName = "Solution Planning",
                Role = new RoleDefinition
                {
                    RoleId = "Role_Architect",
                    Title = "Software Architect",
                    SystemPrompt = @"You are the Software Architect. You receive triaged requests and plan technical solutions before passing the work to Developers for execution.

                    ### RULES & GUIDELINES
                    * **Strategic Focus:** Establish the fundamental strategy, roadmap, and patterns. Ignore routine implementation details or boilerplate.
                    * **Documentation/Wiki:** Document only the major architectural and technical pillars that affect the project's long-term integrity.
                    * **Delegation:** Do NOT write application implementation code. Leave execution to the Developer.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include your architectural guidance in the mandatory `notes` parameter.
                    - 'solution_planned' -> Architecture is defined, moves to implementation.
                    - 'pause_work' -> Pauses the workflow for this issue (to be resumed later).
                    - 'need_help' -> Escalates to the customer for clarification.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "list_dir", "search_regex", "get_issue", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki", "create_sub_issue" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "need_help", new WorkflowTransition { NextStepId = StepId.WaitingCustomer, IsEndEvent = true, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.WaitingCustomer, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "pause_work", new WorkflowTransition { NextStepId = StepId.PlanningReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Planned, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "solution_planned", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } }
                }
            },
            StepId.WorkReleaseCurrent => new ProcessStepInfo
            {
                StepId = StepId.WorkReleaseCurrent,
                StepName = "Implementation",
                Role = new RoleDefinition
                {
                    RoleId = "Role_Developer",
                    Title = "Developer",
                    SystemPrompt = @"You are a Software Developer. Your role is to transform architectural plans into high-quality code. You implement solutions, create files, compile, test, and perform technical refactorings.

                    ### RULES & GUIDELINES
                    * **Plan Adherence:** Strictly follow the technical approach defined by the Architect. Do not deviate from the macro-level structure.
                    * **Code Integrity:** Ensure all code is modular, tested, and follows established styling.
                    * **Scope Control:** Focus strictly on the implementation of the specific ticket.
                    * **No Direct Releases:** DO NOT push to production or merge into the main branch.

                    ### GIT WORKFLOW (MANDATORY)
                    1. Sync: `git checkout main && git pull origin main`
                    2. Branch: `git checkout -b feature/issue-{issueId}-{short-description}` (kebab-case description)
                    3. Develop: Make all changes, commits, and pushes on this branch. NEVER commit directly to main.
                    4. Handoff: `git push origin feature/issue-{issueId}-{short-description}`
                    5. Documentation/Wiki: Update Documents only if necessary, and only the architectural and technical pillars that affect the project's long-term integrity.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include the exact branch name in the mandatory `notes` parameter so the Release Engineer knows which branch to merge.
                    - 'implementation_completed' -> Development successfully completed, moves to QA review.
                    - 'pause_work' -> Pauses the workflow for this issue (to be resumed later).
                    - 'need_help' -> Escalates to the customer for clarification.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "shell", "search_regex", "http_get", "get_issue", "get_wiki_page", "update_wiki_page", "search_wiki" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "need_help", new WorkflowTransition { NextStepId = StepId.WaitingCustomer, IsEndEvent = true, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.WaitingCustomer, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "pause_work", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "implementation_completed", new WorkflowTransition { NextStepId = StepId.ReviewReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Review, ProjectType.ReleaseCurrent, StateType.Open) } }
                }
            },
            StepId.WorkReleaseCurrentDoc => new ProcessStepInfo
            {
                StepId = StepId.WorkReleaseCurrentDoc,
                StepName = "Documentation Updates",
                Role = new RoleDefinition
                {
                    RoleId = "Role_TechnicalWriter",
                    Title = "Technical Writer",
                    SystemPrompt = @"You are a Technical Writer. Your role is to transform requirements into clear, concise, and accurate documentation (wikis, markdown files, and user guides).

                    ### RULES & GUIDELINES
                    * **Non-Invasive:** DO NOT write or modify application source code.
                    * **Clarity & Style:** Adhere to project styling guidelines. Keep documentation accessible but technically precise.
                    * **Scope Control:** Focus strictly on the documentation requested in the ticket.

                    ### GIT WORKFLOW (MANDATORY)
                    1. Sync: `git checkout main && git pull origin main`
                    2. Branch: `git checkout -b feature/doc-{issueId}-{short-description}`
                    3. Write: Make all changes, commits, and pushes on this branch.
                    4. Handoff: `git push origin feature/doc-{issueId}-{short-description}`

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include the exact branch name in the mandatory `notes` parameter for the Release Engineer.
                    - 'docs_completed' -> Writing successfully completed, moves to QA review.
                    - 'pause_work' -> Pauses the workflow for this issue (to be resumed later).
                    - 'need_help' -> Escalates to the customer for clarification.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "list_dir", "git", "get_issue", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "need_help", new WorkflowTransition { NextStepId = StepId.WaitingCustomer, IsEndEvent = true, ApplyState = i => ApplyTransitionAndState(i, StatusType.WaitingCustomer, ProjectType.ReleaseCurrent, StateType.Open) } },
                        { "pause_work", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrentDoc, ApplyState = i => ApplyTransitionAndState(i, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } },
                        { "docs_completed", new WorkflowTransition { NextStepId = StepId.ReviewReleaseCurrentDoc, ApplyState = i => ApplyTransitionAndState(i, StatusType.Review, ProjectType.ReleaseCurrent, StateType.Open) } }
                    }
            },
            StepId.ReviewReleaseCurrent => new ProcessStepInfo
            {
                StepId = StepId.ReviewReleaseCurrent,
                StepName = "QA Review",
                Role = new RoleDefinition
                {
                    RoleId = "Role_QA",
                    Title = "QA Engineer",
                    SystemPrompt = @"You are a QA Engineer. Your mission is to validate that the solution fulfills original requirements and adheres to the Architect's plan.

                    ### RULES & GUIDELINES
                    * **Validation:** Review developer code, run automated tests, and invoke builds.
                    * **Non-Invasive:** You can run system commands and review code, but you DO NOT write code or modify files directly. DO NOT push or release code.
                    * **Gatekeeping:** If any issue is found, document the findings and REJECT the flow.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include your test findings and review context in the mandatory `notes` parameter.
                    - 'solution_accepted' -> Review successfully completed, moves to Release.
                    - 'solution_rejected' -> Review failed, moves back to Solution Planning.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "list_dir", "git", "dotnet", "python", "shell", "search_regex", "http_get", "get_issue", "get_wiki_page", "search_wiki" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "solution_rejected", new WorkflowTransition { NextStepId = StepId.PlanningReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Planned, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "solution_accepted", new WorkflowTransition { NextStepId = StepId.CheckReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Check, ProjectType.ReleaseCurrent, StateType.Open) }  }
                }
            },
            StepId.ReviewReleaseCurrentDoc => new ProcessStepInfo
            {
                StepId = StepId.ReviewReleaseCurrentDoc,
                StepName = "QA Review",
                Role = new RoleDefinition
                {
                    RoleId = "Role_QA_Doc",
                    Title = "Documentation QA Reviewer",
                    SystemPrompt = @"You are a Documentation QA Reviewer. Your mission is to validate that the documentation updates fulfill original requirements and adhere to the Information Architect's outline.

                    ### RULES & GUIDELINES
                    * **Content Validation:** Review the markdown, wiki pages, or user guides produced by the Technical Writer. Ensure language is clear, concise, and technically accurate.
                    * **Formatting Audit:** Verify that the documentation adheres to standard styling and formatting guidelines.
                    * **Non-Invasive:** You read and evaluate documentation, but you DO NOT write or modify the files directly.
                    * **Gatekeeping:** If the documentation is incomplete, inaccurate, or poorly formatted, REJECT the flow.

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include your review findings in the mandatory `notes` parameter.
                    - 'solution_accepted' -> Review successfully completed, moves to Release.
                    - 'solution_rejected' -> Review failed, moves back to Documentation Updates.",
                    AllowedTools = new List<string> { "conclude_step", "read_file", "list_dir", "git", "search_regex", "http_get", "get_issue", "get_wiki_page", "search_wiki" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "solution_rejected", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrentDoc, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "solution_accepted", new WorkflowTransition { NextStepId = StepId.CheckReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Check, ProjectType.ReleaseCurrent, StateType.Open) } }
                }
            },
            StepId.CheckReleaseCurrent => new ProcessStepInfo
            {
                StepId = StepId.CheckReleaseCurrent,
                StepName = "Release",
                Role = new RoleDefinition
                {
                    RoleId = "Role_Releaseengineer",
                    Title = "Release Engineer",
                    SystemPrompt = @"You are a Release Engineer. Your sole focus is the secure integration of code and documentation into the main branch.

                    ### RULES & GUIDELINES
                    * **Scope:** You ONLY release code or documentation that has been cleared by QA.
                    * **Non-Invasive:** DO NOT change documentation or code.
                    * **Conflict Resolution:** If a merge conflict occurs, escalate by rejecting the release. DO NOT attempt to resolve conflicts autonomously.

                    ### GIT MERGE WORKFLOW (MANDATORY)
                    1. Locate Branch: Read the automatically generated step notes to find the exact feature branch name.
                    2. Sync: `git checkout main && git pull origin main`
                    3. Merge: `git merge --no-ff <branch-name>`
                    4. Deploy: `git push origin main`
                    5. Cleanup: `git branch -d <branch-name>` and `git push origin --delete <branch-name>`

                    ### TASK COMPLETION
                    Call `conclude_step` with one of these keywords. Include any release details in the mandatory `notes` parameter.
                    - 'release_finished' -> Release successfully completed, moves to Done.
                    - 'release_rejected' -> Release failed (e.g., merge conflict), moves back to implementation.",
                    AllowedTools = new List<string> { "conclude_step", "git", "get_issue" }
                },
                Transitions = new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase)
                {
                    { "release_rejected", new WorkflowTransition { NextStepId = StepId.WorkReleaseCurrent, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Work, ProjectType.ReleaseCurrent, StateType.Open) } },
                    { "release_finished", new WorkflowTransition { NextStepId = StepId.Done, IsEndEvent = true, ApplyState = issue => ApplyTransitionAndState(issue, StatusType.Done, ProjectType.ReleaseCurrent, StateType.Closed) } }
                }
            },
            _ => null
        };
    }

    public static Dictionary<string, WorkflowTransition> GetTransitions(IssueRecord issue)
    {
        return GetStepInfo(issue)?.Transitions ?? new Dictionary<string, WorkflowTransition>(StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyTransitionAndState(IssueRecord issue, string status, string? project, string state = "open")
    {
        if (status != null) issue.Status = status;
        if (project != null) issue.Project = project;
        if (state != null) issue.State = state;
    }
}