using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class CreateProcessTool : IAboTool
{
    private readonly string _processDirectory;

    public CreateProcessTool()
    {
        _processDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Processes");
        if (!Directory.Exists(_processDirectory))
        {
            Directory.CreateDirectory(_processDirectory);
        }
    }

    public string Name => "create_process";
    public string Description => "Creates a new BPMN process definition. CRITICAL: Every node, step, gateway, and transition MUST have a strict, consistent ID (e.g., Step_ReviewCode, Gateway_IsApproved). You must generate the valid BPMN XML.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            processId = new { type = "string", description = "The unique identifier for the process (e.g., Type_Dev_Sprint)." },
            bpmnXml = new { type = "string", description = "The complete, valid BPMN XML definition representing the process." }
        },
        required = new[] { "processId", "bpmnXml" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        CreateProcessArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<CreateProcessArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return "Failed to parse arguments.";
        }

        if (args == null || string.IsNullOrWhiteSpace(args.ProcessId) || string.IsNullOrWhiteSpace(args.BpmnXml))
            return "Invalid process data provided. processId and bpmnXml are required.";

        var xml = args.BpmnXml.Trim();

        try
        {
            if (!CheckBpmnTool.Validate(xml, out var parseError))
            {
                return $"Error creating process: Invalid BPMN XML. {parseError}";
            }

            var filePath = Path.Combine(_processDirectory, $"{args.ProcessId}.bpmn");

            if (File.Exists(filePath))
            {
                return $"Process '{args.ProcessId}' already exists. Use the update_process tool to modify it.";
            }

            await File.WriteAllTextAsync(filePath, xml);
            return $"Successfully created new process definition '{args.ProcessId}' at {filePath}.";
        }
        catch (Exception ex)
        {
            return $"Error creating process: {ex.Message}";
        }
    }

    private class CreateProcessArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public string BpmnXml { get; set; } = string.Empty;
    }
}
