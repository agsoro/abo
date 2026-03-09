using System.Text.Json;
using System.Text.RegularExpressions;

namespace Abo.Tools;

public abstract class QuizToolBase : IAboTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract object ParametersSchema { get; }

    protected static string SubscriptionsPath => "Data/quiz_subscriptions.json";
    protected static string LeaderboardPath => "Data/quiz_leaderboard.md";

    public abstract Task<string> ExecuteAsync(string argumentsJson);

    protected void EnsureDataDirectory()
    {
        var dir = Path.GetDirectoryName(SubscriptionsPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
    }

    protected async Task<List<string>> LoadSubscriptionsAsync()
    {
        if (!File.Exists(SubscriptionsPath)) return new List<string>();
        var json = await File.ReadAllTextAsync(SubscriptionsPath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    protected async Task SaveSubscriptionsAsync(List<string> subs)
    {
        var json = JsonSerializer.Serialize(subs);
        await File.WriteAllTextAsync(SubscriptionsPath, json);
    }
}

public class SubscribeQuizTool : QuizToolBase
{
    public override string Name => "subscribe_quiz";
    public override string Description => "Subscribes the current channel to the hourly quiz.";

    public override object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            channel_id = new { type = "string", description = "The ID of the channel to subscribe (found in [CONTEXT])." }
        },
        required = new[] { "channel_id" }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, options);
        if (args == null || !args.TryGetValue("channel_id", out var channelId)) return "Error: Missing channel_id.";

        EnsureDataDirectory();
        var subs = await LoadSubscriptionsAsync();
        if (!subs.Contains(channelId))
        {
            subs.Add(channelId);
            await SaveSubscriptionsAsync(subs);
            return $"Successfully subscribed channel {channelId} to the quiz.";
        }
        return "You are already subscribed!";
    }
}

public class UnsubscribeQuizTool : QuizToolBase
{
    public override string Name => "unsubscribe_quiz";
    public override string Description => "Unsubscribes the current channel from the hourly quiz.";

    public override object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            channel_id = new { type = "string", description = "The ID of the channel to unsubscribe (found in [CONTEXT])." }
        },
        required = new[] { "channel_id" }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, options);
        if (args == null || !args.TryGetValue("channel_id", out var channelId)) return "Error: Missing channel_id.";

        EnsureDataDirectory();
        var subs = await LoadSubscriptionsAsync();
        if (subs.Remove(channelId))
        {
            await SaveSubscriptionsAsync(subs);
            return "Successfully unsubscribed.";
        }
        return "You were not subscribed.";
    }
}

public class GetQuizLeaderboardTool : QuizToolBase
{
    public override string Name => "get_quiz_leaderboard";
    public override string Description => "Returns the current quiz rankings.";

    public override object ParametersSchema => new { type = "object", properties = new { } };

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        if (!File.Exists(LeaderboardPath)) return "Leaderboard is empty.";
        return await File.ReadAllTextAsync(LeaderboardPath);
    }
}

public class UpdateQuizScoreTool : QuizToolBase
{
    public override string Name => "update_quiz_score";
    public override string Description => "Adds points to a user's total in the quiz leaderboard.";

    public override object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            user_name = new { type = "string", description = "The name of the user." },
            points = new { type = "integer", description = "Amount of points to add (default 1)." }
        },
        required = new[] { "user_name" }
    };

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var args = JsonSerializer.Deserialize<Dictionary<string, object>>(argumentsJson, options);
        if (args == null || !args.TryGetValue("user_name", out var userNameObj)) return "Error: Missing user_name.";
        
        string userName = userNameObj.ToString()!.Trim(); // Trim for robustness
        int points = 1;
        if (args.TryGetValue("points", out var pointsObj))
        {
            if (pointsObj is JsonElement el) 
            {
                if (el.ValueKind == JsonValueKind.Number) points = el.GetInt32();
                else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var p)) points = p;
            }
            else points = Convert.ToInt32(pointsObj);
        }

        EnsureDataDirectory();
        if (!File.Exists(LeaderboardPath)) 
        {
            await File.WriteAllTextAsync(LeaderboardPath, "# Quiz Leaderboard\n\n| User | Score |\n| --- | --- |\n");
        }

        var lines = (await File.ReadAllLinesAsync(LeaderboardPath)).ToList();
        
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            // Support both @username and plain username
            var match = Regex.Match(lines[i], @"\|\s*(.+?)\s*\|\s*(\d+)\s*\|");
            if (match.Success)
            {
                var existingUser = match.Groups[1].Value.Trim().Replace("@", "");
                var incomingUser = userName.Replace("@", "");

                if (existingUser.Equals(incomingUser, StringComparison.OrdinalIgnoreCase))
                {
                    int currentScore = int.Parse(match.Groups[2].Value);
                    lines[i] = $"| @{incomingUser.ToLower()} | {currentScore + points} |";
                    found = true;
                    break;
                }
            }
        }

        if (!found)
        {
            var cleanName = userName.Replace("@", "").ToLower();
            lines.Add($"| @{cleanName} | {points} |");
        }

        await File.WriteAllLinesAsync(LeaderboardPath, lines);
        return $"Updated leaderboard for @{userName.Replace("@", "").ToLower()}.";
    }
}
