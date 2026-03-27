using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Abo.Core.Services;

/// <summary>
/// Centralized service for logging LLM traffic and consumption data.
/// Provides thread-safe, asynchronous access to log files with automatic trimming.
/// All read and write operations are serialized via SemaphoreSlim to prevent
/// concurrent file handle conflicts (IOException).
/// </summary>
public class TrafficLoggerService
{
    private readonly ILogger<TrafficLoggerService> _logger;
    private readonly string _dataDir;
    private readonly string _trafficLogPath;
    private readonly string _consumptionLogPath;
    private readonly int _maxLogEntries;

    // Serialize all traffic file access (reads and writes) to prevent IOException
    private static readonly SemaphoreSlim _trafficLogLock = new SemaphoreSlim(1, 1);
    // Serialize all consumption file access (reads and writes) to prevent IOException
    private static readonly SemaphoreSlim _consumptionLogLock = new SemaphoreSlim(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _issueFileLocks = new();

    /// <summary>
    /// Initializes a new instance of the TrafficLoggerService.
    /// Ensures the Data directory exists and loads configuration.
    /// </summary>
    public TrafficLoggerService(ILogger<TrafficLoggerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _dataDir = Path.Combine(AppContext.BaseDirectory, "Data");
        _trafficLogPath = Path.Combine(_dataDir, "llm_traffic.jsonl");
        _consumptionLogPath = Path.Combine(_dataDir, "llm_consumption.jsonl");

        // Load configurable max entries (default: 500)
        if (int.TryParse(configuration["Config:MaxLogEntries"], out var maxEntries) && maxEntries > 0)
        {
            _maxLogEntries = maxEntries;
        }
        else
        {
            _maxLogEntries = 500;
        }

        // Ensure Data directory exists
        if (!Directory.Exists(_dataDir))
        {
            Directory.CreateDirectory(_dataDir);
        }
    }

    /// <summary>
    /// Logs a traffic entry (request/response) to the traffic log file.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="type">The type of entry (e.g., "REQUEST", "RESPONSE").</param>
    /// <param name="content">The content to log.</param>
    public async Task LogTrafficAsync(string sessionId, string type, string content)
    {
        try
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                Type = type,
                Content = content
            };
            var line = JsonSerializer.Serialize(logEntry) + Environment.NewLine;

            await _trafficLogLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_trafficLogPath, line);
                await TrimLogFileIfNeededAsync(_trafficLogPath, _maxLogEntries);
            }
            finally
            {
                _trafficLogLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write LLM traffic log.");
        }
    }

    /// <summary>
    /// Logs a consumption entry (token usage/cost) to the consumption log file.
    /// Also accumulates per-issue consumption if issueId is provided.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="model">The model used.</param>
    /// <param name="callCount">Number of API calls.</param>
    /// <param name="inputTokens">Total input tokens.</param>
    /// <param name="outputTokens">Total output tokens.</param>
    /// <param name="totalCost">Total cost.</param>
    /// <param name="issueId">Optional issue ID for per-issue tracking.</param>
    public async Task LogConsumptionAsync(
        string sessionId,
        string model,
        int callCount,
        int inputTokens,
        int outputTokens,
        double totalCost,
        string? issueId = null)
    {
        try
        {
            var logEntry = new
            {
                Timestamp = DateTime.UtcNow,
                SessionId = sessionId,
                Model = model,
                CallCount = callCount,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                TotalCost = totalCost
            };
            var line = JsonSerializer.Serialize(logEntry) + Environment.NewLine;

            await _consumptionLogLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_consumptionLogPath, line);
                await TrimLogFileIfNeededAsync(_consumptionLogPath, _maxLogEntries);
            }
            finally
            {
                _consumptionLogLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write LLM consumption log.");
        }

        if (!string.IsNullOrWhiteSpace(issueId))
        {
            await AccumulateIssueConsumptionAsync(issueId, callCount, totalCost);
        }
    }

    /// <summary>
    /// Retrieves traffic log entries, sorted newest-first with a stable secondary sort
    /// to ensure REQUEST appears before RESPONSE when timestamps match.
    /// Thread-safe: acquires lock to prevent conflicts with concurrent writes.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <returns>List of traffic log entries as JsonElement.</returns>
    public async Task<List<JsonElement>> GetTrafficAsync(int limit)
    {
        if (!File.Exists(_trafficLogPath))
        {
            return new List<JsonElement>();
        }

        await _trafficLogLock.WaitAsync();
        try
        {
            var lines = await File.ReadAllLinesAsync(_trafficLogPath);
            var entries = new List<JsonElement>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<JsonElement>(line);
                    entries.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Return newest entries first with a stable secondary sort.
            // Pair each entry with its original file line-index so we have a stable tiebreaker
            // when two entries share the same Timestamp (e.g. REQUEST and RESPONSE written within
            // the same millisecond). ThenBy(idx) preserves file-write order within a tied timestamp
            // group, ensuring REQUEST (written first, lower idx) always appears before its paired
            // RESPONSE (written second, higher idx) in the newest-first output.
            var indexed = entries
                .Select((entry, idx) => new { entry, idx })
                .ToList();

            var result = indexed
                .OrderByDescending(x =>
                {
                    if (x.entry.TryGetProperty("Timestamp", out var ts))
                        return ts.GetString() ?? "";
                    return "";
                })
                .ThenBy(x => x.idx)   // preserve original write-order (REQUEST before RESPONSE) within same timestamp
                .Take(limit)
                .Select(x => x.entry)
                .ToList();

            return result;
        }
        finally
        {
            _trafficLogLock.Release();
        }
    }

    /// <summary>
    /// Retrieves consumption log entries, sorted newest-first.
    /// Thread-safe: acquires lock to prevent conflicts with concurrent writes.
    /// </summary>
    /// <param name="limit">Maximum number of entries to return.</param>
    /// <returns>List of consumption log entries as JsonElement.</returns>
    public async Task<List<JsonElement>> GetConsumptionAsync(int limit)
    {
        if (!File.Exists(_consumptionLogPath))
        {
            return new List<JsonElement>();
        }

        await _consumptionLogLock.WaitAsync();
        try
        {
            var lines = await File.ReadAllLinesAsync(_consumptionLogPath);
            var entries = new List<JsonElement>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<JsonElement>(line);
                    entries.Add(entry);
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            // Return newest entries first, limited by the limit parameter
            var result = entries.AsEnumerable().Reverse().Take(limit).ToList();
            return result;
        }
        finally
        {
            _consumptionLogLock.Release();
        }
    }

    private static async Task TrimLogFileIfNeededAsync(string filePath, int maxEntries)
    {
        var allLines = (await File.ReadAllLinesAsync(filePath))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (allLines.Length > maxEntries)
        {
            var trimmed = allLines.Skip(allLines.Length - maxEntries);
            await File.WriteAllLinesAsync(filePath, trimmed);
        }
    }

    private async Task AccumulateIssueConsumptionAsync(string issueId, int calls, double cost)
    {
        var lockObj = _issueFileLocks.GetOrAdd(issueId, _ => new SemaphoreSlim(1, 1));
        await lockObj.WaitAsync();
        try
        {
            var dir = Path.Combine(_dataDir, "IssueConsumption");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, $"{issueId}.json");

            IssueConsumptionRecord record;
            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    record = JsonSerializer.Deserialize<IssueConsumptionRecord>(json)
                             ?? new IssueConsumptionRecord { IssueId = issueId };
                }
                catch
                {
                    record = new IssueConsumptionRecord { IssueId = issueId };
                }
            }
            else
            {
                record = new IssueConsumptionRecord { IssueId = issueId };
            }

            record.TotalCalls += calls;
            record.TotalCost += cost;

            await File.WriteAllTextAsync(filePath,
                JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to accumulate issue consumption for issue '{issueId}'.");
        }
        finally
        {
            lockObj.Release();
        }
    }
}

/// <summary>
/// Represents the aggregated consumption data for a specific issue.
/// </summary>
public class IssueConsumptionRecord
{
    public string IssueId { get; set; } = string.Empty;
    public int TotalCalls { get; set; }
    public double TotalCost { get; set; }
}
