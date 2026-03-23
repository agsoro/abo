using Abo.Contracts.OpenAI;

namespace Abo.Tools;

public class GetSystemTimeTool : IAboTool
{
    public string Name => "get_system_time";
    public string Description => "Returns the current UTC time of the host system.";
    
    // An empty object schema as it takes no parameters
    public object ParametersSchema => new
    {
        type = "object",
        properties = new { },
        required = Array.Empty<string>()
    };

    public Task<string> ExecuteAsync(string argumentsJson)
    {
        var time = DateTime.UtcNow.ToString("O");
        return Task.FromResult($"The current system UTC time is: {time}");
    }
}
