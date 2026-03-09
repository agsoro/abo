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
                // Remove oldest messages, but try to keep the system prompt if we were storing it here.
                // However, our Orchestrator adds the system prompt fresh every time.
                // So we just trim the oldest ones.
                history.RemoveRange(0, history.Count - MaxHistoryMessages);
            }
        }
    }

    public void ClearHistory(string sessionId)
    {
        _history.TryRemove(sessionId, out _);
    }
}
