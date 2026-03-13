using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Abo.Core.Connectors;

public class LocalWindowsConnector : IConnector
{
    private readonly ConnectorEnvironment _environment;

    // Shared HttpClient ist .NET Best Practice (vermeidet Socket Exhaustion)
    private static readonly HttpClient _sharedHttpClient = new HttpClient();

    /// <summary>Maximale Response-Body-Größe in Bytes (100 KB).</summary>
    private const int MaxResponseSizeBytes = 100 * 1024;

    /// <summary>Maximaler Timeout in Sekunden für HTTP-Requests.</summary>
    private const int MaxTimeoutSeconds = 120;

    public LocalWindowsConnector(ConnectorEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(environment.Dir))
        {
            throw new ArgumentException("Environment directory cannot be empty.");
        }

        // Ensure base directory exists
        if (!Directory.Exists(environment.Dir))
        {
            Directory.CreateDirectory(environment.Dir);
        }

        _environment = environment;
    }

    private string GetFullPath(string relativePath)
    {
        // Prevent directory traversal attacks
        var combinedPath = Path.GetFullPath(Path.Combine(_environment.Dir, relativePath));
        if (!combinedPath.StartsWith(Path.GetFullPath(_environment.Dir)))
        {
            throw new UnauthorizedAccessException("Cannot access paths outside the environment directory.");
        }
        return combinedPath;
    }

    public async Task<string> ReadFileAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!File.Exists(path)) return $"Error: File '{relativePath}' not found.";
        try
        {
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > 50 * 1024)
            {
                return $"Error: File '{relativePath}' exceeds the 50KB limit. Please ask the IT department for help.";
            }

            return await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    public async Task<string> WriteFileAsync(string relativePath, string content)
    {
        var path = GetFullPath(relativePath);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(path, content);
            return $"Successfully wrote to '{relativePath}'.";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    public Task<string> DeleteFileAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!File.Exists(path)) return Task.FromResult($"Error: File '{relativePath}' not found.");
        try
        {
            File.Delete(path);
            return Task.FromResult($"Successfully deleted '{relativePath}'.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error deleting file: {ex.Message}");
        }
    }

    public Task<string> ListDirAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        if (!Directory.Exists(path)) return Task.FromResult($"Error: Directory '{relativePath}' not found.");
        try
        {
            var directories = Directory.GetDirectories(path).Select(Path.GetFileName).Select(d => $"[DIR]  {d}");
            var files = Directory.GetFiles(path).Select(Path.GetFileName).Select(f => $"       {f}");

            var allItems = directories.Concat(files);
            return Task.FromResult(string.Join("\n", allItems));
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error listing directory: {ex.Message}");
        }
    }

    public Task<string> MkDirAsync(string relativePath)
    {
        var path = GetFullPath(relativePath);
        try
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                return Task.FromResult($"Successfully created directory '{relativePath}'.");
            }
            return Task.FromResult($"Directory '{relativePath}' already exists.");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error creating directory: {ex.Message}");
        }
    }

    public async Task<string> RunGitAsync(string arguments)
    {
        return await RunProcessAsync("git", arguments);
    }

    public async Task<string> RunDotnetAsync(string arguments)
    {
        return await RunProcessAsync("dotnet", arguments);
    }

    public async Task<string> RunPythonAsync(string arguments)
    {
        return await RunProcessAsync("python", arguments);
    }

    private async Task<string> RunProcessAsync(string command, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = _environment.Dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                return $"Error ({process.ExitCode}): {error}\n{output}";
            }

            return string.IsNullOrWhiteSpace(output) ? "Command executed successfully with no output." : output;
        }
        catch (Exception ex)
        {
            return $"Failed to start process '{command}': {ex.Message}";
        }
    }

    public async Task<string> SearchRegexAsync(string relativePath, string pattern, int limitLinesPerFile)
    {
        var basePath = GetFullPath(relativePath);
        if (!Directory.Exists(basePath) && !File.Exists(basePath))
        {
            return $"Error: Path '{relativePath}' not found.";
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            return $"Error: Invalid regex pattern '{pattern}'. {ex.Message}";
        }

        var results = new StringBuilder();
        var filesToSearch = new List<string>();

        if (File.Exists(basePath))
        {
            filesToSearch.Add(basePath);
        }
        else
        {
            try
            {
                filesToSearch.AddRange(Directory.GetFiles(basePath, "*.*", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                return $"Error reading directory '{relativePath}': {ex.Message}";
            }
        }

        foreach (var file in filesToSearch)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > 500 * 1024) continue; // Skip large files > 500KB

                var relativeFilePath = Path.GetRelativePath(_environment.Dir, file);
                var fileName = Path.GetFileName(file);
                bool fileMatches = regex.IsMatch(fileName);

                var matchedLines = new List<(int LineNumber, string Content)>();

                var lines = await File.ReadAllLinesAsync(file);
                int lineNumber = 1;
                foreach (var line in lines)
                {
                    if (regex.IsMatch(line))
                    {
                        matchedLines.Add((lineNumber, line));
                        if (matchedLines.Count >= limitLinesPerFile)
                        {
                            break;
                        }
                    }
                    lineNumber++;
                }

                if (fileMatches || matchedLines.Any())
                {
                    var fileResult = new StringBuilder();
                    fileResult.AppendLine($"File: {relativeFilePath}");
                    if (fileMatches)
                    {
                        fileResult.AppendLine("  -> Filename matches pattern");
                    }

                    bool limitReached = false;
                    foreach (var match in matchedLines)
                    {
                        var lineText = $"  Line {match.LineNumber}: {match.Content}";
                        if (fileResult.Length + lineText.Length > 10 * 1024)
                        {
                            limitReached = true;
                            break;
                        }
                        fileResult.AppendLine(lineText);
                    }

                    if (limitReached)
                    {
                        fileResult.AppendLine("  -> Error: Search result for this file exceeded the 10KB limit. Please ask the IT department for help.");
                    }

                    fileResult.AppendLine();
                    results.Append(fileResult.ToString());
                }
            }
            catch
            {
                // Ignore files we cannot read (e.g., binaries, access denied)
            }
        }

        if (results.Length == 0)
        {
            return "No matches found.";
        }

        return results.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // HTTP GET Tool Implementation — FA-01 bis FA-08 + Sicherheitsmaßnahmen
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<string> HttpGetAsync(
        string url,
        Dictionary<string, string>? headers = null,
        int timeoutSeconds = 30)
    {
        // FA-08 / NFA-04 – Schema-Validierung: nur http/https erlaubt
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return $"Error (Invalid URL): URL must start with http:// or https://. Got: {url}";
        }

        // FA-08 – URL-Parsbarkeit prüfen
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"Error (Invalid URL): Could not parse URL: {url}";
        }

        // SSRF-Schutz: delegiert an HttpGetSecurityHelper (RFC-1918 + Loopback)
        var ssrfError = await HttpGetSecurityHelper.CheckSsrfAsync(uri);
        if (ssrfError != null)
        {
            return ssrfError;
        }

        // FA-05 – Timeout auf MaxTimeoutSeconds deckeln, Minimum 1 Sekunde
        var effectiveTimeout = Math.Max(1, Math.Min(timeoutSeconds, MaxTimeoutSeconds));

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(effectiveTimeout));
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            // FA-04 – Optionale Headers hinzufügen (mit Sicherheits-Filterung)
            if (headers != null)
            {
                foreach (var (key, value) in headers)
                {
                    // Header Injection Schutz: Geblockte System-Header nicht weiterleiten
                    if (!HttpGetSecurityHelper.IsRestrictedHeader(key))
                    {
                        request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
            }

            // FA-01 – Request senden (ResponseHeadersRead: nur Headers laden, Body lazily)
            using var response = await _sharedHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token
            );

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "unknown";

            // FA-07 – Response-Body mit Hard-Cap auf MaxResponseSizeBytes lesen
            var bodyBytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            string body;
            bool wasTruncated = bodyBytes.Length > MaxResponseSizeBytes;

            body = wasTruncated
                ? Encoding.UTF8.GetString(bodyBytes, 0, MaxResponseSizeBytes)
                : Encoding.UTF8.GetString(bodyBytes);

            var truncationHint = wasTruncated
                ? $"\n\n[... Response truncated at {MaxResponseSizeBytes / 1024} KB. Original size: {bodyBytes.Length / 1024} KB ...]"
                : string.Empty;

            // FA-06 – Nicht-2xx Antworten als Fehler formatieren
            if (!response.IsSuccessStatusCode)
            {
                return $"Error (HTTP {(int)response.StatusCode}): {response.ReasonPhrase}\n" +
                       $"URL: {url}\n\n{body}{truncationHint}";
            }

            // FA-02 / FA-03 – Erfolgreiche Antwort: Status + Content-Type + Body
            return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n" +
                   $"Content-Type: {contentType}\n\n{body}{truncationHint}";
        }
        catch (OperationCanceledException)
        {
            return $"Error (Timeout): Request exceeded {effectiveTimeout} seconds timeout.\nURL: {url}";
        }
        catch (HttpRequestException ex)
        {
            return $"Error (Network): {ex.Message}\nURL: {url}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}\nURL: {url}";
        }
    }
}
