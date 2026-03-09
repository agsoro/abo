using System.Text.Json;
using System.Text.RegularExpressions;
using Abo.Services;

namespace Abo.Tools;

public abstract class QuizToolBase : IAboTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract object ParametersSchema { get; }

    protected static string SubscriptionsPath => "Data/Quiz/quiz_subscriptions.json";
    protected static string LeaderboardPath => "Data/Quiz/leaderboard.json";

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
    private readonly UserService _userService;

    public SubscribeQuizTool(UserService userService)
    {
        _userService = userService;
    }

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

        var user = _userService.GetOrCreateUser(channelId);
        if (!user.IsSubscribedToQuiz)
        {
            user.IsSubscribedToQuiz = true;
            _userService.UpdateUser(user);
            return $"Successfully subscribed channel {channelId} to the quiz.";
        }
        return "You are already subscribed!";
    }
}

public class UnsubscribeQuizTool : QuizToolBase
{
    private readonly UserService _userService;

    public UnsubscribeQuizTool(UserService userService)
    {
        _userService = userService;
    }

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

        var user = _userService.GetOrCreateUser(channelId);
        if (user.IsSubscribedToQuiz)
        {
            user.IsSubscribedToQuiz = false;
            _userService.UpdateUser(user);
            return "Successfully unsubscribed.";
        }
        return "You were not subscribed.";
    }
}

public class GetQuizLeaderboardTool : QuizToolBase
{
    public override string Name => "get_quiz_leaderboard";
    public override string Description => "Returns the current quiz rankings, Optionally filtered by topic.";

    public override object ParametersSchema => new 
    { 
        type = "object", 
        properties = new 
        { 
            topic = new { type = "string", description = "Optional topic to filter rankings by." }
        } 
    };

    public override async Task<string> ExecuteAsync(string argumentsJson)
    {
        if (!File.Exists(LeaderboardPath)) return "Leaderboard is empty.";
        
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var json = await File.ReadAllTextAsync(LeaderboardPath);
        var data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json, options);
        if (data == null || data.Count == 0) return "Leaderboard is empty.";

        var args = JsonSerializer.Deserialize<Dictionary<string, string>>(argumentsJson, options);
        string? topic = null;
        if (args != null && args.TryGetValue("topic", out var topicValue))
        {
            topic = topicValue;
        }

        topic = topic?.ToLowerInvariant();
        var output = "";

        if (!string.IsNullOrWhiteSpace(topic))
        {
            if (!data.ContainsKey(topic)) return $"No rankings for topic '{topic}'.";
            output += $"# {topic.ToUpper()} Quiz Leaderboard\n\n| User | Score |\n| --- | --- |\n";
            foreach (var kvp in data[topic].OrderByDescending(x => x.Value))
            {
                output += $"| @{kvp.Key} | {kvp.Value} |\n";
            }
        }
        else
        {
            foreach (var t in data.Keys)
            {
                output += $"# {t.ToUpper()} Quiz Leaderboard\n\n| User | Score |\n| --- | --- |\n";
                foreach (var kvp in data[t].OrderByDescending(x => x.Value))
                {
                    output += $"| @{kvp.Key} | {kvp.Value} |\n";
                }
                output += "\n";
            }
        }

        return output;
    }
}

public class UpdateQuizScoreTool : QuizToolBase
{
    public override string Name => "update_quiz_score";
    public override string Description => "Adds points to a user's total in the quiz leaderboard for a specific topic.";

    public override object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            user_name = new { type = "string", description = "The name of the user." },
            topic = new { type = "string", description = "The topic pool the question was from." },
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
        string topic = "general";
        if (args.TryGetValue("topic", out var topicObj) && topicObj != null) 
        {
            topic = topicObj.ToString()!.ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(topic)) topic = "general";

        int points = 1;
        if (args.TryGetValue("points", out var pointsObj) && pointsObj != null)
        {
            if (pointsObj is JsonElement el) 
            {
                if (el.ValueKind == JsonValueKind.Number) points = el.GetInt32();
                else if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var p)) points = p;
            }
            else points = Convert.ToInt32(pointsObj);
        }

        EnsureDataDirectory();
        
        Dictionary<string, Dictionary<string, int>> data = new();
        if (File.Exists(LeaderboardPath)) 
        {
            var json = await File.ReadAllTextAsync(LeaderboardPath);
            try 
            {
                data = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(json, options) ?? new();
            }
            catch
            {
                // In case it's the old Markdown format
                data = new();
            }
        }

        var cleanName = userName.Replace("@", "").ToLower();
        
        if (!data.ContainsKey(topic)) data[topic] = new Dictionary<string, int>();

        if (data[topic].ContainsKey(cleanName))
        {
            data[topic][cleanName] += points;
        }
        else
        {
            data[topic][cleanName] = points;
        }

        await File.WriteAllTextAsync(LeaderboardPath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        return $"Updated leaderboard: @{cleanName} has {data[topic][cleanName]} points in the '{topic}' pool.";
    }
}
