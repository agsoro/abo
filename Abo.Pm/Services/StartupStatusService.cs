namespace Abo.Services;

/// <summary>
/// Singleton service designed to capture and centralize configuration errors 
/// occurring during application startup (e.g., missing environments or unreachable external systems).
/// </summary>
public class StartupStatusService
{
    public List<string> Errors { get; } = new();

    public void AddError(string error)
    {
        lock (Errors)
        {
            Errors.Add(error);
        }
    }
}
