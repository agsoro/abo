using System;
using System.Collections.Generic;

namespace Abo.Core.Core
{
    public class RoleDefinition
    {
        public string RoleId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SystemPrompt { get; set; } = string.Empty;
        public List<string> AllowedTools { get; set; } = new();
    }

    public static class AvailableRoles
    {
        public static readonly List<RoleDefinition> AllRoles = new()
        {
            new RoleDefinition
            {
                RoleId = "Role_Productmanager",
                Title = "Product Manager",
                SystemPrompt = "You are the Product Manager. Your primary goal is to oversee features, check triage requests and plan if and when a issue should be worked on. You engage with the issue tracker actively. DO NOT write code or modify files directly.",
                AllowedTools = new List<string> { "list_issues", "get_issue", "add_issue_comment", "get_wiki_page", "read_file", "list_dir", "search_wiki" }
            },
            new RoleDefinition
            {
                RoleId = "Role_Architect",
                Title = "Software Architect",
                SystemPrompt = "You are the Software Architect. You receive triaged requests and plan technical solutions. You claim tickets, outline required changes, document findings in the wiki, and define the technical approach before passing the work to Developers.",
                AllowedTools = new List<string> { "read_file", "list_dir", "search_regex", "get_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" }
            },
            new RoleDefinition
            {
                RoleId = "Role_Developer",
                Title = "Developer",
                SystemPrompt = "You are a Software Developer. You implement solutions according to architectural plans, write code, create files, compile, test, and perform technical refactorings.",
                AllowedTools = new List<string> { "read_file", "write_file", "delete_file", "list_dir", "mkdir", "git", "dotnet", "python", "search_regex", "http_get", "get_issue", "add_issue_comment", "get_wiki_page", "update_wiki_page", "search_wiki" }
            },
            new RoleDefinition
            {
                RoleId = "Role_QA",
                Title = "QA Engineer",
                SystemPrompt = "You are a QA Engineer. You review developer code, run automated tests, invoke builds, and ensure the solution fulfills original requirements. You should update the wiki/documentation with final notes. You can run system commands and review code, but you DO NOT write code or modify files directly. If an issue is found, document it and reject the flow.",
                AllowedTools = new List<string> { "read_file", "list_dir", "git", "dotnet", "python", "search_regex", "http_get", "get_issue", "add_issue_comment", "get_wiki_page", "create_wiki_page", "update_wiki_page", "search_wiki" }
            },
            new RoleDefinition
            {
                RoleId = "Role_Releaseengineer",
                Title = "Release Engineer",
                SystemPrompt = "You are a Release Engineer. You push/merge/rebase/release the code. You DO NOT change documentation or code. You only release the code.",
                AllowedTools = new List<string> { "read_file", "list_dir", "git", "get_issue", "add_issue_comment" }
            }
        };
    }
}
