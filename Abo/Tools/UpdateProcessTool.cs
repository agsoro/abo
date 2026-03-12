using System.Text.Json;
using Abo.Tools;

namespace Abo.Tools;

public class UpdateProcessTool : IAboTool
{
    private readonly string _processDirectory;

    public UpdateProcessTool()
    {
        _processDirectory = Path.Combine(AppContext.BaseDirectory, "Data", "Processes");
        if (!Directory.Exists(_processDirectory))
        {
            Directory.CreateDirectory(_processDirectory);
        }
    }

    public string Name => "update_process";
    public string Description => "Updates an existing BPMN process definition. Overwrites the previous definition with the new BPMN XML. It keeps the same strict ID naming scheme.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            processId = new { type = "string", description = "The unique identifier for the process (e.g., Type_Dev_Sprint)." },
            bpmnXml = new { type = "string", description = "The new, updated and complete BPMN XML definition." }
        },
        required = new[] { "processId", "bpmnXml" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        UpdateProcessArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<UpdateProcessArgs>(argumentsJson, jsOptions);
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
                return $"Error updating process: Invalid BPMN XML. {parseError}";
            }

            var filePath = Path.Combine(_processDirectory, $"{args.ProcessId}.bpmn");

            if (!File.Exists(filePath))
            {
                return $"Process '{args.ProcessId}' does not exist. Use the create_process tool first.";
            }

            await File.WriteAllTextAsync(filePath, xml);
            return $"Successfully updated process definition '{args.ProcessId}' at {filePath}.";
        }
        catch (Exception ex)
        {
            return $"Error updating process: {ex.Message}";
        }
    }

    private class UpdateProcessArgs
    {
        public string ProcessId { get; set; } = string.Empty;
        public string BpmnXml { get; set; } = string.Empty;
    }
}
