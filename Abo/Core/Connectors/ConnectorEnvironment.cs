namespace Abo.Core.Connectors;

public class ConnectorEnvironment
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "local"; // local, remote, etc.
    public string Os { get; set; } = "win"; // win, linux, etc.
    public string Dir { get; set; } = string.Empty;
}
