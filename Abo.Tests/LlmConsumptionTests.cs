using System.Text.Json;
using Xunit;
using Abo.Contracts.OpenAI;

namespace Abo.Tests;

/// <summary>
/// Unit tests for the UsageInfo deserialization from LLM API responses (ABO-0004).
/// </summary>
[Trait("Category", "Unit")]
public class LlmConsumptionTests
{
    [Fact]
    public void UsageInfo_Deserializes_FullResponse_Correctly()
    {
        // Arrange
        var json = """
        {
            "id": "gen-123",
            "choices": [
                {
                    "index": 0,
                    "message": { "role": "assistant", "content": "Hello!" },
                    "finish_reason": "stop"
                }
            ],
            "usage": {
                "prompt_tokens": 500,
                "completion_tokens": 150,
                "total_tokens": 650,
                "cost": 0.0025
            }
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Usage);
        Assert.Equal(500, response.Usage.PromptTokens);
        Assert.Equal(150, response.Usage.CompletionTokens);
        Assert.Equal(650, response.Usage.TotalTokens);
        Assert.Equal(0.0025, response.Usage.Cost);
    }

    [Fact]
    public void UsageInfo_IsNull_WhenNotPresent_InResponse()
    {
        // Arrange – some endpoints do not return usage
        var json = """
        {
            "id": "gen-456",
            "choices": [
                {
                    "index": 0,
                    "message": { "role": "assistant", "content": "Hi!" },
                    "finish_reason": "stop"
                }
            ]
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        // Assert
        Assert.NotNull(response);
        Assert.Null(response.Usage);
    }

    [Fact]
    public void UsageInfo_Cost_IsNullable_WhenNotProvided()
    {
        // Arrange – cost field missing (some providers don't report it)
        var json = """
        {
            "id": "gen-789",
            "choices": [],
            "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 50,
                "total_tokens": 150
            }
        }
        """;

        // Act
        var response = JsonSerializer.Deserialize<ChatCompletionResponse>(json);

        // Assert
        Assert.NotNull(response?.Usage);
        Assert.Equal(100, response!.Usage!.PromptTokens);
        Assert.Equal(50, response.Usage.CompletionTokens);
        Assert.Equal(150, response.Usage.TotalTokens);
        Assert.Null(response.Usage.Cost); // Cost is optional
    }

    [Fact]
    public void UsageInfo_TotalTokens_EqualsPromptPlusCompletion()
    {
        // Arrange
        var usage = new UsageInfo
        {
            PromptTokens = 1000,
            CompletionTokens = 300,
            TotalTokens = 1300,
            Cost = 0.005
        };

        // Assert
        Assert.Equal(usage.PromptTokens + usage.CompletionTokens, usage.TotalTokens);
    }
}
