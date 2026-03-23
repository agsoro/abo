namespace Abo.Tools;

public interface IAboTool
{
    string Name { get; }
    string Description { get; }
    object ParametersSchema { get; }

    Task<string> ExecuteAsync(string argumentsJson);
}
