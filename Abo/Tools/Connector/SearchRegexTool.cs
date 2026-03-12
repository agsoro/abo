using System.Text.Json;
using Abo.Core.Connectors;

namespace Abo.Tools.Connector;

public class SearchRegexTool : IAboTool
{
    private readonly IConnector _connector;

    public SearchRegexTool(IConnector connector)
    {
        _connector = connector;
    }

    public string Name => "search_regex";
    public string Description => "Searches for a regex pattern within filenames and file contents across the specified directory.";

    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            searchPath = new { type = "string", description = "The relative path to search within (use '.' or empty string for the project root)." },
            pattern = new { type = "string", description = "The regex pattern to search for." },
            limitLinesPerFile = new { type = "integer", description = "Max matching lines to return per file. Defaults to 10." }
        },
        required = new[] { "searchPath", "pattern" },
        additionalProperties = false
    };

    public async Task<string> ExecuteAsync(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("searchPath", out var pathElement) && root.TryGetProperty("pattern", out var patternElement))
            {
                var searchPath = pathElement.GetString() ?? ".";
                if (string.IsNullOrWhiteSpace(searchPath)) searchPath = ".";

                var pattern = patternElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(pattern)) return "Error: pattern parameter cannot be empty.";

                int limitLines = 10;
                if (root.TryGetProperty("limitLinesPerFile", out var limitElement) && limitElement.TryGetInt32(out var parsedLimit))
                {
                    if (parsedLimit > 0)
                    {
                        limitLines = parsedLimit;
                    }
                }

                return await _connector.SearchRegexAsync(searchPath, pattern, limitLines);
            }

            return "Error: searchPath and pattern parameters are required.";
        }
        catch (Exception ex)
        {
            return $"Error parsing arguments: {ex.Message}";
        }
    }
}
