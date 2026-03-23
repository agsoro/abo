using System.Text.Json;
using System.Xml.Linq;
using Abo.Tools;
using System.Xml;

namespace Abo.Tools;

public class CheckBpmnTool : IAboTool
{
    public string Name => "check_bpmn";
    public string Description => "Checks if the provided BPMN XML string is well-formed and can be parsed. Use this tool BEFORE saving process definitions if you are unsure.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            bpmnXml = new { type = "string", description = "The BPMN XML definition to validate." }
        },
        required = new[] { "bpmnXml" },
        additionalProperties = false
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        CheckBpmnArgs? args;
        try
        {
            args = JsonSerializer.Deserialize<CheckBpmnArgs>(argumentsJson, jsOptions);
        }
        catch
        {
            return Task.FromResult("Failed to parse arguments.");
        }

        if (args == null || string.IsNullOrWhiteSpace(args.BpmnXml))
            return Task.FromResult("Invalid BPMN XML provided.");

        var isValid = Validate(args.BpmnXml, out var errorMessage);

        if (isValid)
            return Task.FromResult("BPMN XML is valid and parser-friendly.");

        return Task.FromResult($"BPMN XML is invalid. Error: {errorMessage}");
    }

    /// <summary>
    /// Validates the XML syntax natively.
    /// </summary>
    public static bool Validate(string bpmnXml, out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            // Simple XDocument parsing is sufficient to catch mismatched or unclosed tags.
            XDocument.Parse(bpmnXml);
            return true;
        }
        catch (XmlException ex)
        {
            errorMessage = $"unparsable content detected; this may indicate an invalid BPMN 2.0 diagram file line: {ex.LineNumber} column: {ex.LinePosition} nested error: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private class CheckBpmnArgs
    {
        public string BpmnXml { get; set; } = string.Empty;
    }
}
