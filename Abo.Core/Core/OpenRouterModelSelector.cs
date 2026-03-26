using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Abo.Core;

/// <summary>
/// Automatically selects the best OpenRouter models based on the Artificial Analysis Coding Index.
/// Uses combinatorial optimization to maximize intelligence within an allowable estimated usage budget.
/// </summary>
public class OpenRouterModelSelector
{
    private readonly ILogger<OpenRouterModelSelector> _logger;
    private DateTime _lastUpdate = DateTime.MinValue;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public OpenRouterModelSelector(ILogger<OpenRouterModelSelector> logger)
    {
        _logger = logger;
    }

    private class ModelCandidate
    {
        public string Id { get; set; } = string.Empty;
        public double Score { get; set; }
        public double PricePrompt { get; set; }
        public double PriceCompletion { get; set; }
        public string Vendor { get; set; } = string.Empty;
    }

    private class ModelCombination
    {
        public ModelCandidate Review { get; set; } = null!;
        public ModelCandidate Capable { get; set; } = null!;
        public ModelCandidate Generic { get; set; } = null!;
        public double TotalCost { get; set; }
        public double TotalScore { get; set; }
    }

    public async Task UpdateModelsIfRequiredAsync(string appSettingsPath)
    {
        await _updateLock.WaitAsync();
        try
        {
            if ((DateTime.UtcNow - _lastUpdate).TotalHours < 12)
            {
                return;
            }

            _logger.LogInformation("Updating OpenRouter model selections based on Artificial Analysis coding index scores & Combinatorial Cost Optimization...");
            
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Abo-Agent");

            var modelsResponseString = await httpClient.GetStringAsync("https://openrouter.ai/api/v1/models");
            var modelsDoc = JsonDocument.Parse(modelsResponseString);
            
            var allModelsInfo = new Dictionary<string, (double Prompt, double Completion)>();
            foreach (var element in modelsDoc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = element.GetProperty("id").GetString()!;
                double promptPrice = 0.0, completionPrice = 0.0;
                
                if (element.TryGetProperty("pricing", out var pricingEl))
                {
                    if (pricingEl.TryGetProperty("prompt", out var pEl) && pEl.GetString() is string pStr)
                        double.TryParse(pStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out promptPrice);

                    if (pricingEl.TryGetProperty("completion", out var cEl) && cEl.GetString() is string cStr)
                        double.TryParse(cStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out completionPrice);
                }
                allModelsInfo[id] = (promptPrice, completionPrice);
            }

            var candidateIds = allModelsInfo.Keys
                .Where(k => k.StartsWith("openai/") || k.StartsWith("anthropic/") || 
                            k.StartsWith("google/") || k.StartsWith("meta-llama/") || 
                            k.StartsWith("mistralai/") || k.StartsWith("deepseek/") ||
                            k.StartsWith("cohere/") || k.StartsWith("x-ai/") || k.StartsWith("qwen/"))
                .ToList();

            var benchmarkResults = new ConcurrentBag<(string Id, double Score)>();
            var semaphore = new SemaphoreSlim(20);
            using var internalClient = new HttpClient();
            internalClient.DefaultRequestHeaders.Add("User-Agent", "Abo-Agent");
            
            var tasks = candidateIds.Select(async id =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var url = $"https://openrouter.ai/api/internal/v1/artificial-analysis-benchmarks?slug={id}";
                    var responseStr = await internalClient.GetStringAsync(url);
                    var doc = JsonDocument.Parse(responseStr);
                    
                    if (doc.RootElement.TryGetProperty("data", out var dataArr) && dataArr.GetArrayLength() > 0)
                    {
                        if (dataArr[0].TryGetProperty("benchmark_data", out var bData) && bData.TryGetProperty("evaluations", out var evals))
                        {
                            if (evals.TryGetProperty("artificial_analysis_coding_index", out var scoreEl))
                            {
                                benchmarkResults.Add((id, scoreEl.GetDouble()));
                            }
                        }
                    }
                }
                catch { }
                finally { semaphore.Release(); }
            });

            await Task.WhenAll(tasks);

            var rankedModels = benchmarkResults
                .OrderByDescending(x => x.Score)
                .Select(x => new ModelCandidate
                {
                    Id = x.Id,
                    Score = x.Score,
                    PricePrompt = allModelsInfo[x.Id].Prompt,
                    PriceCompletion = allModelsInfo[x.Id].Completion,
                    Vendor = x.Id.Split('/')[0]
                })
                .Where(m => m.Score > 0 && (m.PricePrompt > 0 || m.PriceCompletion > 0))
                .ToList();

            if (rankedModels.Count == 0) 
            {
                _logger.LogWarning("No benchmark data returned from OpenRouter. Cannot update models.");
                return;
            }

            // Usage limits (in million prompt tokens estimated)
            const double modelUsageM = 2.0;    // Generic
            const double capableUsageM = 20.0; // Capable
            const double reviewUsageM = 3.0;   // Review

            var top3Review = rankedModels.Take(3).ToList();
            var top7Capable = rankedModels.Take(7).ToList();
            var top10Generic = rankedModels.Take(10).ToList();

            var combinations = new List<ModelCombination>();

            foreach (var rev in top3Review)
            {
                foreach (var cap in top7Capable)
                {
                    if (cap.Vendor == rev.Vendor) continue; // Must be different vendor

                    foreach (var gen in top10Generic)
                    {
                        double cost = (rev.PricePrompt * 1000000.0 * reviewUsageM) + 
                                      (cap.PricePrompt * 1000000.0 * capableUsageM) +
                                      (gen.PricePrompt * 1000000.0 * modelUsageM);

                        double score = rev.Score + cap.Score + gen.Score;

                        combinations.Add(new ModelCombination {
                            Review = rev,
                            Capable = cap,
                            Generic = gen,
                            TotalCost = cost,
                            TotalScore = score
                        });
                    }
                }
            }

            if (combinations.Count == 0)
            {
                _logger.LogWarning("Fallback applied: No combinations found matching differential vendor criteria.");
                return;
            }

            // Find absolute cheapest
            var absoluteCheapest = combinations.OrderBy(c => c.TotalCost).First();
            double maxBudget = absoluteCheapest.TotalCost * 1.07; // Allow 7% overhead for better intelligence

            var bestConfig = combinations
                .Where(c => c.TotalCost <= maxBudget)
                .OrderByDescending(c => c.TotalScore)
                .ThenBy(c => c.TotalCost)
                .First();

            var reviewModel = bestConfig.Review;
            var capableModel = bestConfig.Capable;
            var modelNameCandidate = bestConfig.Generic;

            _logger.LogInformation($"Combinatorics Config Cost: ${bestConfig.TotalCost:F2} (Max Allowed Budget: ${maxBudget:F2})");
            _logger.LogInformation($"Selected ReviewModelName (Top 3): {reviewModel.Id} (Score: {reviewModel.Score:F1}, Input/M: ${(reviewModel.PricePrompt * 1000000):F2})");
            _logger.LogInformation($"Selected CapableModelName (Top 7 diff Vendor): {capableModel.Id} (Score: {capableModel.Score:F1}, Input/M: ${(capableModel.PricePrompt * 1000000):F2})");
            _logger.LogInformation($"Selected ModelName (Top 10): {modelNameCandidate.Id} (Score: {modelNameCandidate.Score:F1}, Input/M: ${(modelNameCandidate.PricePrompt * 1000000):F2})");

            // Persist the changes to appsettings.json
            if (File.Exists(appSettingsPath))
            {
                var json = await File.ReadAllTextAsync(appSettingsPath);
                var jNode = JsonNode.Parse(json);
                if (jNode != null && jNode["Config"] is JsonObject configNode)
                {
                    configNode["ModelName"] = modelNameCandidate.Id;
                    configNode["CapableModelName"] = capableModel.Id;
                    configNode["ReviewModelName"] = reviewModel.Id;
                    
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    await File.WriteAllTextAsync(appSettingsPath, jNode.ToJsonString(options));
                    _logger.LogInformation("Successfully updated appsettings.json with combinatorial models.");
                }
            }
            else
            {
                _logger.LogWarning($"Could not find appsettings.json at path: {appSettingsPath}");
            }
            
            _lastUpdate = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update OpenRouter models via combinatorics.");
        }
        finally
        {
            _updateLock.Release();
        }
    }
}
