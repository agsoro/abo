using System.Collections.Generic;

namespace Abo.Contracts.Models;

public class RoleDefinition
{
    public string RoleId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public List<string> AllowedTools { get; set; } = new();
}
