using System.Collections.Concurrent;
using Abo.Contracts.OpenAI;

namespace Abo.Core;

/// <summary>
/// A simple in-memory session store for conversation history.
/// </summary>
public class SessionService
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _history = new();
    private const int MaxHistoryMessages = 20;

    public List<ChatMessage> GetHistory(string sessionId)
    {
        return _history.GetOrAdd(sessionId, _ => new List<ChatMessage>());
    }

    public void AddMessage(string sessionId, ChatMessage message)
    {
        var history = GetHistory(sessionId);
        lock (history)
        {
            history.Add(message);

            // Keep history lean
            if (history.Count > MaxHistoryMessages)
            {
                int excess = history.Count - MaxHistoryMessages;
                int removeCount = excess;

                // Advance removeCount to the next 'user' message to ensure we do not break tool chains
                // Anthropic API will throw an error if a tool_result does not have a corresponding tool_calls block
                while (removeCount < history.Count && history[removeCount].Role != "user")
                {
                    removeCount++;
                }

                if (removeCount < history.Count)
                {
                    history.RemoveRange(0, removeCount);
                }
            }
        }
    }

    public void ClearHistory(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
    }
}
