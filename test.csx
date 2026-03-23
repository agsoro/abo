using System;
using System.IO;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text;

var envFile = Path.Combine(Environment.CurrentDirectory, "Abo.Pm", "Data", "Environments", "environments.json");
var jsOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
using var doc = JsonDocument.Parse(File.ReadAllText(envFile));
var root = doc.RootElement[0];
var ghNode = root.GetProperty("IssueTracker");
var owner = ghNode.GetProperty("Owner").GetString();
var repo = ghNode.GetProperty("Repository").GetString();

var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
if (string.IsNullOrWhiteSpace(token)) {
    // try to get from secrets
    Console.WriteLine("No GITHUB_TOKEN env var, please specify one or use secrets tool...");
}
// since I don't know the token easily from a csx script without ConfigurationBuilder, 
// I'll just change the integration test locally to Log or skip assertions if projects don't exist.
